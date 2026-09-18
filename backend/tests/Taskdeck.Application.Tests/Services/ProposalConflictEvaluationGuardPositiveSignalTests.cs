using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public sealed class ProposalConflictEvaluationGuardPositiveSignalTests
{
    [Fact]
    public async Task DetectConflictsAsync_MixedValidAndUnevaluableOperations_SuppressesCapacityAndFreshnessEvidence()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        var targetColumnId = Guid.NewGuid();
        var card = new Card(cardId, boardId, Guid.NewGuid(), "Source card");
        var targetColumn = new Column(boardId, "Ready", position: 1, wipLimit: 3);
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            userId,
            "Review mixed operation safety",
            RiskLevel.High,
            Guid.NewGuid().ToString(),
            boardId);
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id,
            sequence: 0,
            actionType: "move",
            targetType: "card",
            parameters: $"{{\"columnId\":\"{targetColumnId}\"}}",
            idempotencyKey: Guid.NewGuid().ToString(),
            targetId: cardId.ToString()));
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id,
            sequence: 1,
            actionType: "create",
            targetType: "card",
            parameters: "{\"destination\":\"unsupported\"}",
            idempotencyKey: Guid.NewGuid().ToString()));

        var unitOfWork = new Mock<IUnitOfWork>();
        var proposals = new Mock<IAutomationProposalRepository>();
        var cards = new Mock<ICardRepository>();
        var columns = new Mock<IColumnRepository>();
        var comments = new Mock<ICardCommentRepository>();
        var webhooks = new Mock<IOutboundWebhookSubscriptionRepository>();
        var authorization = new Mock<IAuthorizationService>();

        unitOfWork.Setup(unit => unit.AutomationProposals).Returns(proposals.Object);
        unitOfWork.Setup(unit => unit.Cards).Returns(cards.Object);
        unitOfWork.Setup(unit => unit.Columns).Returns(columns.Object);
        unitOfWork.Setup(unit => unit.CardComments).Returns(comments.Object);
        unitOfWork.Setup(unit => unit.OutboundWebhookSubscriptions).Returns(webhooks.Object);
        proposals
            .Setup(repository => repository.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);
        proposals
            .Setup(repository => repository.GetPendingByOperationTargetAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        cards
            .Setup(repository => repository.GetByIdAsync(cardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);
        columns
            .Setup(repository => repository.GetByIdWithCardsAsync(targetColumnId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetColumn);
        comments
            .Setup(repository => repository.CountByCardIdAsync(cardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        webhooks
            .Setup(repository => repository.GetActiveByBoardAsync(boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        authorization
            .Setup(service => service.CanReadBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Success(true));

        var inner = new ProposalConflictDetector(unitOfWork.Object, authorization.Object);
        var guard = new ProposalConflictEvaluationGuard(
            inner,
            unitOfWork.Object,
            Mock.Of<ILogger<ProposalConflictEvaluationGuard>>());

        var result = await guard.DetectConflictsAsync(proposal.Id, userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain(row =>
            row.Key == "unable-to-evaluate-operation" && row.Tone == ConflictTone.Warn);
        result.Value.Should().Contain(row => row.Key == "high-risk" && row.Tone == ConflictTone.Warn);
        result.Value.Should().NotContain(row => row.Key is "capacity" or "fresh-data" or "status");
        result.Value.Should().NotContain(row => row.Tone == ConflictTone.Ok);
    }
}
