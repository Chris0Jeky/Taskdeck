using FluentAssertions;
using Moq;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

/// <summary>
/// Pins the authorization scope passed by the two review surfaces that consume
/// revision-aware related-proposal evidence. Broad It.IsAny scope setups would
/// allow a board-backed read to drift into owner-private history (or the reverse)
/// without failing the service tests (#3249).
/// </summary>
public sealed class ProposalEvidenceScopeContractTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConflictDetector_PassesExactBoardOrOwnerScope(bool boardScoped)
    {
        var ownerId = Guid.NewGuid();
        var boardId = boardScoped ? Guid.NewGuid() : (Guid?)null;
        var targetBoardId = boardId ?? Guid.NewGuid();
        var cardId = Guid.NewGuid();
        var proposal = CreateProposal(ownerId, boardId, cardId);
        var expectedScope = new ProposalEvidenceScope(boardId, ownerId);
        ProposalEvidenceScope? observedScope = null;

        var proposals = new Mock<IAutomationProposalRepository>();
        proposals
            .Setup(repository => repository.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        var cards = new Mock<ICardRepository>();
        cards
            .Setup(repository => repository.GetByIdAsync(cardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Card(cardId, targetBoardId, Guid.NewGuid(), "Evidence target"));

        var comments = new Mock<ICardCommentRepository>();
        comments
            .Setup(repository => repository.CountByCardIdAsync(cardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var webhooks = new Mock<IOutboundWebhookSubscriptionRepository>();
        if (boardId.HasValue)
        {
            webhooks
                .Setup(repository => repository.GetActiveByBoardAsync(boardId.Value, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<OutboundWebhookSubscription>());
        }

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(work => work.AutomationProposals).Returns(proposals.Object);
        unitOfWork.SetupGet(work => work.Cards).Returns(cards.Object);
        unitOfWork.SetupGet(work => work.Columns).Returns(Mock.Of<IColumnRepository>());
        unitOfWork.SetupGet(work => work.CardComments).Returns(comments.Object);
        unitOfWork.SetupGet(work => work.OutboundWebhookSubscriptions).Returns(webhooks.Object);

        var authorization = new Mock<IAuthorizationService>();
        if (boardId.HasValue)
        {
            authorization
                .Setup(service => service.CanReadBoardAsync(ownerId, boardId.Value))
                .ReturnsAsync(Result.Success(true));
        }

        var relatedEvidence = new Mock<IRelatedProposalEvidenceService>();
        relatedEvidence
            .Setup(service => service.HasOtherPendingProposalTargetingCardAsync(
                It.IsAny<ProposalEvidenceScope>(),
                proposal.Id,
                cardId,
                It.IsAny<CancellationToken>()))
            .Callback<ProposalEvidenceScope, Guid, Guid, CancellationToken>(
                (scope, _, _, _) => observedScope = scope)
            .ReturnsAsync(false);

        var detector = new ProposalConflictDetector(
            unitOfWork.Object,
            authorization.Object,
            relatedEvidence.Object);

        var result = await detector.DetectConflictsAsync(proposal.Id, ownerId);

        result.IsSuccess.Should().BeTrue();
        observedScope.Should().Be(expectedScope);
        relatedEvidence.Verify(service => service.HasOtherPendingProposalTargetingCardAsync(
            expectedScope,
            proposal.Id,
            cardId,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CardHistory_PassesExactBoardOrOwnerScope(bool boardScoped)
    {
        var ownerId = Guid.NewGuid();
        var boardId = boardScoped ? Guid.NewGuid() : (Guid?)null;
        var cardId = Guid.NewGuid();
        var proposal = CreateProposal(ownerId, boardId, cardId);
        var expectedScope = new ProposalEvidenceScope(boardId, ownerId);
        ProposalEvidenceScope? observedScope = null;

        var proposals = new Mock<IAutomationProposalRepository>();
        proposals
            .Setup(repository => repository.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        var auditLogs = new Mock<IAuditLogRepository>();
        auditLogs
            .Setup(repository => repository.GetByEntityAsync(
                "Card",
                cardId,
                200,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AuditLog>());

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(work => work.AutomationProposals).Returns(proposals.Object);
        unitOfWork.SetupGet(work => work.AuditLogs).Returns(auditLogs.Object);

        var relatedEvidence = new Mock<IRelatedProposalEvidenceService>();
        relatedEvidence
            .Setup(service => service.GetLatestOtherProposalTargetingCardAsync(
                It.IsAny<ProposalEvidenceScope>(),
                proposal.Id,
                cardId,
                It.IsAny<CancellationToken>()))
            .Callback<ProposalEvidenceScope, Guid, Guid, CancellationToken>(
                (scope, _, _, _) => observedScope = scope)
            .ReturnsAsync((AutomationProposal?)null);

        var service = new CardHistoryService(unitOfWork.Object, relatedEvidence.Object);

        var result = await service.GetCardHistoryForProposalAsync(proposal.Id);

        result.IsSuccess.Should().BeTrue();
        observedScope.Should().Be(expectedScope);
        relatedEvidence.Verify(evidence => evidence.GetLatestOtherProposalTargetingCardAsync(
            expectedScope,
            proposal.Id,
            cardId,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static AutomationProposal CreateProposal(Guid ownerId, Guid? boardId, Guid cardId)
    {
        var proposal = new AutomationProposal(
            ProposalSourceType.Queue,
            ownerId,
            "Scope contract",
            RiskLevel.Low,
            Guid.NewGuid().ToString("N"),
            boardId);
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id,
            0,
            "update",
            "card",
            "{}",
            Guid.NewGuid().ToString("N"),
            cardId.ToString("D")));
        return proposal;
    }
}
