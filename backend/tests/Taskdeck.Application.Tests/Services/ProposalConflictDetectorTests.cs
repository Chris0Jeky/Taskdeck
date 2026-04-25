using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class ProposalConflictDetectorTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IAutomationProposalRepository> _proposalRepoMock;
    private readonly Mock<ICardRepository> _cardRepoMock;
    private readonly Mock<IColumnRepository> _columnRepoMock;
    private readonly Mock<ICardCommentRepository> _commentRepoMock;
    private readonly Mock<IOutboundWebhookSubscriptionRepository> _webhookRepoMock;
    private readonly Mock<IAuthorizationService> _authServiceMock;
    private readonly ProposalConflictDetector _detector;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _boardId = Guid.NewGuid();

    public ProposalConflictDetectorTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _proposalRepoMock = new Mock<IAutomationProposalRepository>();
        _cardRepoMock = new Mock<ICardRepository>();
        _columnRepoMock = new Mock<IColumnRepository>();
        _commentRepoMock = new Mock<ICardCommentRepository>();
        _webhookRepoMock = new Mock<IOutboundWebhookSubscriptionRepository>();
        _authServiceMock = new Mock<IAuthorizationService>();

        _unitOfWorkMock.Setup(u => u.AutomationProposals).Returns(_proposalRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Cards).Returns(_cardRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Columns).Returns(_columnRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.CardComments).Returns(_commentRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.OutboundWebhookSubscriptions).Returns(_webhookRepoMock.Object);

        _detector = new ProposalConflictDetector(
            _unitOfWorkMock.Object,
            _authServiceMock.Object);
    }

    #region Authorization and Not Found

    [Fact]
    public async Task DetectConflictsAsync_ProposalNotFound_ReturnsNotFound()
    {
        _proposalRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AutomationProposal?)null);

        var result = await _detector.DetectConflictsAsync(Guid.NewGuid(), _userId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task DetectConflictsAsync_OwnerHasAccess()
    {
        var proposal = CreateProposal(_userId, _boardId);
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);
        SetupNoConflicts();

        var result = await _detector.DetectConflictsAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task DetectConflictsAsync_NonOwnerWithBoardAccess_Succeeds()
    {
        var ownerId = Guid.NewGuid();
        var proposal = CreateProposal(ownerId, _boardId);
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);
        _authServiceMock.Setup(a => a.CanReadBoardAsync(_userId, _boardId))
            .ReturnsAsync(Result.Success(true));
        SetupNoConflicts();

        var result = await _detector.DetectConflictsAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task DetectConflictsAsync_NonOwnerWithoutBoardAccess_ReturnsForbidden()
    {
        var ownerId = Guid.NewGuid();
        var proposal = CreateProposal(ownerId, _boardId);
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);
        _authServiceMock.Setup(a => a.CanReadBoardAsync(_userId, _boardId))
            .ReturnsAsync(Result.Success(false));

        var result = await _detector.DetectConflictsAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task DetectConflictsAsync_NonOwnerNoBoardScope_ReturnsForbidden()
    {
        var ownerId = Guid.NewGuid();
        var proposal = CreateProposal(ownerId, boardId: null);
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        var result = await _detector.DetectConflictsAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
    }

    #endregion

    #region No Conflicts (Ok)

    [Fact]
    public async Task DetectConflictsAsync_NoOperations_ReturnsOkRow()
    {
        var proposal = CreateProposal(_userId, _boardId);
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);
        SetupNoConflicts();

        var result = await _detector.DetectConflictsAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value[0].Tone.Should().Be(ConflictTone.Ok);
        result.Value[0].Key.Should().Be("status");
        result.Value[0].Value.Should().Be("No conflicts detected");
    }

    #endregion

    #region Warn: Stale Data

    [Fact]
    public async Task DetectConflictsAsync_CardModifiedAfterProposal_ReturnsStaleWarning()
    {
        var cardId = Guid.NewGuid();
        var proposal = CreateProposalWithCardOp(_userId, _boardId, cardId, "move");
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        // Card updated after proposal was created
        var card = new Card(_boardId, Guid.NewGuid(), "Test Card");
        // Touch the card to move its UpdatedAt ahead
        card.Update(title: "Updated Title");

        _cardRepoMock.Setup(r => r.GetByIdAsync(cardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);
        SetupEmptySecondaryChecks(proposal);
        SetupNoDuplicateProposal(cardId);

        var result = await _detector.DetectConflictsAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain(r => r.Tone == ConflictTone.Warn && r.Key == "stale-data");
    }

    [Fact]
    public async Task DetectConflictsAsync_CardDeleted_ReturnsMissingTargetWarning()
    {
        var cardId = Guid.NewGuid();
        var proposal = CreateProposalWithCardOp(_userId, _boardId, cardId, "update");
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);
        _cardRepoMock.Setup(r => r.GetByIdAsync(cardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Card?)null);
        SetupEmptySecondaryChecks(proposal);
        SetupNoDuplicateProposal(cardId);

        var result = await _detector.DetectConflictsAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain(r => r.Tone == ConflictTone.Warn && r.Key == "missing-target");
    }

    #endregion

    #region Warn: WIP Limit

    [Fact]
    public async Task DetectConflictsAsync_ColumnAtWipLimit_ReturnsWipWarning()
    {
        var columnId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        var card = CreateCard(cardId);
        var proposal = CreateProposalWithMoveOp(_userId, _boardId, cardId, columnId);
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        // Column with WIP limit of 2 and 2 cards already
        var column = new Column(_boardId, "In Progress", 1, wipLimit: 2);
        AddCardsToColumn(column, 2);

        _columnRepoMock.Setup(r => r.GetByIdWithCardsAsync(columnId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(column);

        _cardRepoMock.Setup(r => r.GetByIdAsync(cardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);
        SetupEmptySecondaryChecks(proposal, cardId);

        var result = await _detector.DetectConflictsAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain(r => r.Tone == ConflictTone.Warn && r.Key == "wip-limit");
    }

    [Fact]
    public async Task DetectConflictsAsync_ColumnBelowWipLimit_NoWipWarning()
    {
        var columnId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        var card = CreateCard(cardId);
        var proposal = CreateProposalWithMoveOp(_userId, _boardId, cardId, columnId);
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        // Column with WIP limit of 5 and 2 cards
        var column = new Column(_boardId, "In Progress", 1, wipLimit: 5);
        AddCardsToColumn(column, 2);

        _columnRepoMock.Setup(r => r.GetByIdWithCardsAsync(columnId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(column);

        _cardRepoMock.Setup(r => r.GetByIdAsync(cardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);
        SetupEmptySecondaryChecks(proposal, cardId);

        var result = await _detector.DetectConflictsAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(r => r.Key == "wip-limit");
    }

    #endregion

    #region Warn: Duplicate Pending Proposals

    [Fact]
    public async Task DetectConflictsAsync_AnotherPendingProposalForSameCard_ReturnsDuplicateWarning()
    {
        var cardId = Guid.NewGuid();
        // Create card BEFORE proposal so card.UpdatedAt <= proposal.CreatedAt
        var card = CreateCard(cardId);
        var proposal = CreateProposalWithCardOp(_userId, _boardId, cardId, "update");
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        _cardRepoMock.Setup(r => r.GetByIdAsync(cardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);
        _webhookRepoMock.Setup(r => r.GetActiveByBoardAsync(_boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OutboundWebhookSubscription>());
        _commentRepoMock.Setup(r => r.GetByCardIdAsync(cardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CardComment>());

        // Another pending proposal for the same card -- must be set up AFTER other mocks
        // because SetupEmptySecondaryChecks would override with It.IsAny<string>()
        var otherProposal = CreateProposal(_userId, _boardId);
        _proposalRepoMock.Setup(r => r.GetPendingByOperationTargetAsync(
                "card", cardId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AutomationProposal> { otherProposal });

        var result = await _detector.DetectConflictsAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain(r => r.Tone == ConflictTone.Warn && r.Key == "duplicate-proposal");
    }

    [Fact]
    public async Task DetectConflictsAsync_SameProposalFoundByTarget_NoDuplicateWarning()
    {
        var cardId = Guid.NewGuid();
        // Create card BEFORE proposal so card.UpdatedAt <= proposal.CreatedAt
        var card = CreateCard(cardId);
        var proposal = CreateProposalWithCardOp(_userId, _boardId, cardId, "update");
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        // Same proposal returned by target query (not a duplicate)
        _proposalRepoMock.Setup(r => r.GetPendingByOperationTargetAsync(
                "card", cardId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AutomationProposal> { proposal });

        _cardRepoMock.Setup(r => r.GetByIdAsync(cardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);
        SetupEmptySecondaryChecks(proposal);

        var result = await _detector.DetectConflictsAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(r => r.Key == "duplicate-proposal");
    }

    #endregion

    #region Warn: High Risk

    [Fact]
    public async Task DetectConflictsAsync_HighRiskProposal_ReturnsHighRiskWarning()
    {
        var proposal = CreateProposal(_userId, _boardId, riskLevel: RiskLevel.High);
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);
        SetupNoConflicts();

        var result = await _detector.DetectConflictsAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain(r => r.Tone == ConflictTone.Warn && r.Key == "high-risk");
    }

    [Fact]
    public async Task DetectConflictsAsync_CriticalRiskProposal_ReturnsHighRiskWarning()
    {
        var proposal = CreateProposal(_userId, _boardId, riskLevel: RiskLevel.Critical);
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);
        SetupNoConflicts();

        var result = await _detector.DetectConflictsAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain(r => r.Tone == ConflictTone.Warn && r.Key == "high-risk");
    }

    [Fact]
    public async Task DetectConflictsAsync_LowRiskProposal_NoHighRiskWarning()
    {
        var proposal = CreateProposal(_userId, _boardId, riskLevel: RiskLevel.Low);
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);
        SetupNoConflicts();

        var result = await _detector.DetectConflictsAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(r => r.Key == "high-risk");
    }

    [Fact]
    public async Task DetectConflictsAsync_MediumRiskProposal_NoHighRiskWarning()
    {
        var proposal = CreateProposal(_userId, _boardId, riskLevel: RiskLevel.Medium);
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);
        SetupNoConflicts();

        var result = await _detector.DetectConflictsAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(r => r.Key == "high-risk");
    }

    #endregion

    #region Info: Webhooks

    [Fact]
    public async Task DetectConflictsAsync_ActiveWebhooks_ReturnsWebhookInfo()
    {
        var proposal = CreateProposal(_userId, _boardId);
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        var webhook = new OutboundWebhookSubscription(
            _boardId, _userId, "https://example.com/hook", "secret123");
        _webhookRepoMock.Setup(r => r.GetActiveByBoardAsync(_boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OutboundWebhookSubscription> { webhook });

        var result = await _detector.DetectConflictsAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain(r => r.Tone == ConflictTone.Info && r.Key == "webhooks");
    }

    [Fact]
    public async Task DetectConflictsAsync_NoWebhooks_NoWebhookInfo()
    {
        var proposal = CreateProposal(_userId, _boardId);
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        _webhookRepoMock.Setup(r => r.GetActiveByBoardAsync(_boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OutboundWebhookSubscription>());

        var result = await _detector.DetectConflictsAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(r => r.Key == "webhooks");
    }

    [Fact]
    public async Task DetectConflictsAsync_NoBoardId_NoWebhookCheck()
    {
        var proposal = CreateProposal(_userId, boardId: null);
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);
        SetupNoConflicts();

        var result = await _detector.DetectConflictsAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
        _webhookRepoMock.Verify(
            r => r.GetActiveByBoardAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region Info: Active Comments

    [Fact]
    public async Task DetectConflictsAsync_CardHasComments_ReturnsCommentsInfo()
    {
        var cardId = Guid.NewGuid();
        var card = CreateCard(cardId);
        var proposal = CreateProposalWithCardOp(_userId, _boardId, cardId, "update");
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        _cardRepoMock.Setup(r => r.GetByIdAsync(cardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);

        var comment = new CardComment(cardId, _boardId, _userId, "Some comment");
        _commentRepoMock.Setup(r => r.GetByCardIdAsync(cardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CardComment> { comment });

        SetupNoDuplicateProposal(cardId);
        _webhookRepoMock.Setup(r => r.GetActiveByBoardAsync(_boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OutboundWebhookSubscription>());

        var result = await _detector.DetectConflictsAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain(r => r.Tone == ConflictTone.Info && r.Key == "active-comments");
    }

    [Fact]
    public async Task DetectConflictsAsync_CardHasNoComments_NoCommentsInfo()
    {
        var cardId = Guid.NewGuid();
        var card = CreateCard(cardId);
        var proposal = CreateProposalWithCardOp(_userId, _boardId, cardId, "update");
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        _cardRepoMock.Setup(r => r.GetByIdAsync(cardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);

        _commentRepoMock.Setup(r => r.GetByCardIdAsync(cardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CardComment>());

        SetupNoDuplicateProposal(cardId);
        _webhookRepoMock.Setup(r => r.GetActiveByBoardAsync(_boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OutboundWebhookSubscription>());

        var result = await _detector.DetectConflictsAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(r => r.Key == "active-comments");
    }

    #endregion

    #region Info: Multiple Operations on Same Card

    [Fact]
    public async Task DetectConflictsAsync_MultipleOpsOnSameCard_ReturnsMultiOpInfo()
    {
        var cardId = Guid.NewGuid();
        var card = CreateCard(cardId);
        var proposal = CreateProposal(_userId, _boardId);
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id, 0, "update", "card", "{}", Guid.NewGuid().ToString(), cardId.ToString()));
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id, 1, "move", "card", "{}", Guid.NewGuid().ToString(), cardId.ToString()));

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        _cardRepoMock.Setup(r => r.GetByIdAsync(cardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);

        _commentRepoMock.Setup(r => r.GetByCardIdAsync(cardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CardComment>());
        SetupNoDuplicateProposal(cardId);
        _webhookRepoMock.Setup(r => r.GetActiveByBoardAsync(_boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OutboundWebhookSubscription>());

        var result = await _detector.DetectConflictsAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain(r => r.Tone == ConflictTone.Info && r.Key == "multi-op");
    }

    [Fact]
    public async Task DetectConflictsAsync_SingleOpPerCard_NoMultiOpInfo()
    {
        var cardId = Guid.NewGuid();
        var card = CreateCard(cardId);
        var proposal = CreateProposalWithCardOp(_userId, _boardId, cardId, "update");
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        _cardRepoMock.Setup(r => r.GetByIdAsync(cardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);

        _commentRepoMock.Setup(r => r.GetByCardIdAsync(cardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CardComment>());
        SetupNoDuplicateProposal(cardId);
        _webhookRepoMock.Setup(r => r.GetActiveByBoardAsync(_boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OutboundWebhookSubscription>());

        var result = await _detector.DetectConflictsAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(r => r.Key == "multi-op");
    }

    #endregion

    #region Ok: Positive Signals

    [Fact]
    public async Task DetectConflictsAsync_FreshCardWithOtherWarnings_ReturnsFreshDataOkRow()
    {
        var cardId = Guid.NewGuid();
        var card = CreateCard(cardId);
        // High risk to trigger a warning, so positive signals are also emitted
        var proposal = CreateProposalWithCardOp(_userId, _boardId, cardId, "update", riskLevel: RiskLevel.High);
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        _cardRepoMock.Setup(r => r.GetByIdAsync(cardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);

        _commentRepoMock.Setup(r => r.GetByCardIdAsync(cardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CardComment>());
        SetupNoDuplicateProposal(cardId);
        _webhookRepoMock.Setup(r => r.GetActiveByBoardAsync(_boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OutboundWebhookSubscription>());

        var result = await _detector.DetectConflictsAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain(r => r.Tone == ConflictTone.Ok && r.Key == "fresh-data");
    }

    [Fact]
    public async Task DetectConflictsAsync_ColumnHasCapacity_ReturnsCapacityOkRow()
    {
        var columnId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        var card = CreateCard(cardId);
        // High risk to trigger a warning, so positive signals are also emitted
        var proposal = CreateProposalWithMoveOp(_userId, _boardId, cardId, columnId, riskLevel: RiskLevel.High);
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        // Column with WIP limit of 5 and 2 cards (has capacity)
        var column = new Column(_boardId, "In Progress", 1, wipLimit: 5);
        AddCardsToColumn(column, 2);
        _columnRepoMock.Setup(r => r.GetByIdWithCardsAsync(columnId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(column);

        _cardRepoMock.Setup(r => r.GetByIdAsync(cardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);
        SetupEmptySecondaryChecks(proposal, cardId);

        var result = await _detector.DetectConflictsAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain(r => r.Tone == ConflictTone.Ok && r.Key == "capacity");
    }

    #endregion

    #region Sorting

    [Fact]
    public async Task DetectConflictsAsync_MultipleRows_SortedByTone_WarnFirst()
    {
        var cardId = Guid.NewGuid();
        var card = CreateCard(cardId);
        // High risk proposal targeting a card with comments
        var proposal = CreateProposalWithCardOp(_userId, _boardId, cardId, "update", riskLevel: RiskLevel.High);
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        _cardRepoMock.Setup(r => r.GetByIdAsync(cardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);

        var comment = new CardComment(cardId, _boardId, _userId, "test");
        _commentRepoMock.Setup(r => r.GetByCardIdAsync(cardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CardComment> { comment });
        SetupNoDuplicateProposal(cardId);
        _webhookRepoMock.Setup(r => r.GetActiveByBoardAsync(_boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OutboundWebhookSubscription>());

        var result = await _detector.DetectConflictsAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Count.Should().BeGreaterThan(1);

        // Warn should come before Info, Info before Ok
        var tones = result.Value.Select(r => r.Tone).ToList();
        tones.Should().BeInAscendingOrder();
    }

    #endregion

    #region Combination

    [Fact]
    public async Task DetectConflictsAsync_MultipleConflictsDetected_ReturnsAllRows()
    {
        var cardId = Guid.NewGuid();
        // Create card BEFORE proposal so card.UpdatedAt <= proposal.CreatedAt
        var card = CreateCard(cardId);
        // High risk + card with comments + webhooks
        var proposal = CreateProposalWithCardOp(_userId, _boardId, cardId, "update", riskLevel: RiskLevel.Critical);
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        _cardRepoMock.Setup(r => r.GetByIdAsync(cardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);

        // Has comments
        _commentRepoMock.Setup(r => r.GetByCardIdAsync(cardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CardComment> { new CardComment(cardId, _boardId, _userId, "test") });

        // Has webhooks
        var webhook = new OutboundWebhookSubscription(
            _boardId, _userId, "https://example.com/hook", "secret123");
        _webhookRepoMock.Setup(r => r.GetActiveByBoardAsync(_boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OutboundWebhookSubscription> { webhook });

        SetupNoDuplicateProposal(cardId);

        var result = await _detector.DetectConflictsAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
        // Should have: high-risk (warn), active-comments (info), webhooks (info), fresh-data (ok)
        result.Value.Should().Contain(r => r.Key == "high-risk");
        result.Value.Should().Contain(r => r.Key == "active-comments");
        result.Value.Should().Contain(r => r.Key == "webhooks");
        result.Value.Should().Contain(r => r.Key == "fresh-data");
    }

    #endregion

    #region Expired Proposal Handling

    [Fact]
    public async Task DetectConflictsAsync_ExpiredProposal_StillReturnsConflicts()
    {
        // Expired proposals can still have their conflicts inspected
        var proposal = CreateProposal(_userId, _boardId, expiryMinutes: 1);
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);
        SetupNoConflicts();

        var result = await _detector.DetectConflictsAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task DetectConflictsAsync_OperationWithInvalidTargetId_SkipsGracefully()
    {
        var proposal = CreateProposal(_userId, _boardId);
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id, 0, "update", "card", "{}", Guid.NewGuid().ToString(), "not-a-guid"));

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);
        _webhookRepoMock.Setup(r => r.GetActiveByBoardAsync(_boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OutboundWebhookSubscription>());

        var result = await _detector.DetectConflictsAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task DetectConflictsAsync_OperationWithNullTargetId_SkipsGracefully()
    {
        var proposal = CreateProposal(_userId, _boardId);
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id, 0, "create", "card", "{}", Guid.NewGuid().ToString(), targetId: null));

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);
        _webhookRepoMock.Setup(r => r.GetActiveByBoardAsync(_boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OutboundWebhookSubscription>());

        var result = await _detector.DetectConflictsAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task DetectConflictsAsync_MalformedParametersJson_SkipsColumnParsing()
    {
        var cardId = Guid.NewGuid();
        var card = CreateCard(cardId);
        var proposal = CreateProposal(_userId, _boardId);
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id, 0, "move", "card", "not-json{", Guid.NewGuid().ToString(), cardId.ToString()));

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        _cardRepoMock.Setup(r => r.GetByIdAsync(cardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);

        _commentRepoMock.Setup(r => r.GetByCardIdAsync(cardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CardComment>());
        SetupNoDuplicateProposal(cardId);
        _webhookRepoMock.Setup(r => r.GetActiveByBoardAsync(_boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OutboundWebhookSubscription>());

        var result = await _detector.DetectConflictsAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task DetectConflictsAsync_CreateCardWithPreAssignedId_NoStaleOrMissingWarning()
    {
        var cardId = Guid.NewGuid();
        var proposal = CreateProposal(_userId, _boardId);
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id, 0, "create", "card", "{}", Guid.NewGuid().ToString(), cardId.ToString()));

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);
        _webhookRepoMock.Setup(r => r.GetActiveByBoardAsync(_boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OutboundWebhookSubscription>());

        // Card does NOT exist yet (create operation) -- should NOT produce missing-target warning
        _cardRepoMock.Setup(r => r.GetByIdAsync(cardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Card?)null);

        var result = await _detector.DetectConflictsAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(r => r.Key == "missing-target");
        result.Value.Should().NotContain(r => r.Key == "stale-data");
    }

    [Fact]
    public async Task DetectConflictsAsync_ColumnTargetWithNoParameters_StillDetected()
    {
        var columnId = Guid.NewGuid();
        var proposal = CreateProposal(_userId, _boardId, riskLevel: RiskLevel.High);
        // Column-targeted operation with minimal parameters (domain requires non-empty)
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id, 0, "archive", "column", "{}", Guid.NewGuid().ToString(), columnId.ToString()));

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        var column = new Column(_boardId, "Done", 3, wipLimit: 10);
        _columnRepoMock.Setup(r => r.GetByIdWithCardsAsync(columnId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(column);
        _webhookRepoMock.Setup(r => r.GetActiveByBoardAsync(_boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OutboundWebhookSubscription>());

        var result = await _detector.DetectConflictsAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
        // Column should be detected and produce a capacity Ok signal
        result.Value.Should().Contain(r => r.Key == "capacity");
    }

    [Fact]
    public async Task DetectConflictsAsync_NonStringColumnIdInJson_DoesNotThrow()
    {
        var cardId = Guid.NewGuid();
        var card = CreateCard(cardId);
        var proposal = CreateProposal(_userId, _boardId);
        // columnId is a number, not a string -- should not throw InvalidOperationException
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id, 0, "move", "card", "{\"columnId\": 12345}", Guid.NewGuid().ToString(), cardId.ToString()));

        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);
        _cardRepoMock.Setup(r => r.GetByIdAsync(cardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);
        _commentRepoMock.Setup(r => r.GetByCardIdAsync(cardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CardComment>());
        SetupNoDuplicateProposal(cardId);
        _webhookRepoMock.Setup(r => r.GetActiveByBoardAsync(_boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OutboundWebhookSubscription>());

        var result = await _detector.DetectConflictsAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region Helpers

    private AutomationProposal CreateProposal(
        Guid userId,
        Guid? boardId,
        RiskLevel riskLevel = RiskLevel.Low,
        int expiryMinutes = 1440)
    {
        return new AutomationProposal(
            ProposalSourceType.Chat,
            userId,
            "Test proposal",
            riskLevel,
            Guid.NewGuid().ToString(),
            boardId,
            expiryMinutes: expiryMinutes);
    }

    private AutomationProposal CreateProposalWithCardOp(
        Guid userId,
        Guid boardId,
        Guid cardId,
        string actionType,
        RiskLevel riskLevel = RiskLevel.Low)
    {
        var proposal = CreateProposal(userId, boardId, riskLevel);
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id, 0, actionType, "card", "{}", Guid.NewGuid().ToString(), cardId.ToString()));
        return proposal;
    }

    private AutomationProposal CreateProposalWithMoveOp(
        Guid userId,
        Guid boardId,
        Guid cardId,
        Guid targetColumnId,
        RiskLevel riskLevel = RiskLevel.Low)
    {
        var proposal = CreateProposal(userId, boardId, riskLevel);
        var parameters = $"{{\"columnId\":\"{targetColumnId}\"}}";
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id, 0, "move", "card", parameters, Guid.NewGuid().ToString(), cardId.ToString()));
        return proposal;
    }

    /// <summary>
    /// Creates a card with a known ID. Note: When used after a proposal is
    /// created, the card's UpdatedAt will be slightly after the proposal's
    /// CreatedAt (due to DateTimeOffset.UtcNow in both constructors).
    /// For "fresh card" semantics, create the card BEFORE the proposal.
    /// </summary>
    private Card CreateCard(Guid cardId, string title = "Test Card")
    {
        return new Card(cardId, _boardId, Guid.NewGuid(), title);
    }

    private void SetupNoConflicts()
    {
        _webhookRepoMock.Setup(r => r.GetActiveByBoardAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OutboundWebhookSubscription>());
    }

    private void SetupEmptySecondaryChecks(AutomationProposal proposal, Guid? specificCardId = null)
    {
        _webhookRepoMock.Setup(r => r.GetActiveByBoardAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OutboundWebhookSubscription>());
        _commentRepoMock.Setup(r => r.GetByCardIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CardComment>());

        if (specificCardId.HasValue)
        {
            SetupNoDuplicateProposal(specificCardId.Value);
        }
        else
        {
            // Setup for any card ID
            _proposalRepoMock.Setup(r => r.GetPendingByOperationTargetAsync(
                    "card", It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<AutomationProposal>());
        }
    }

    private void SetupNoDuplicateProposal(Guid cardId)
    {
        _proposalRepoMock.Setup(r => r.GetPendingByOperationTargetAsync(
                "card", cardId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AutomationProposal>());
    }

    private void SetupCardForMove(AutomationProposal proposal, Guid? specificCardId = null)
    {
        // For move operations, we still need the card check for stale data
        if (specificCardId.HasValue)
        {
            var card = CreateCard(specificCardId.Value);
            _cardRepoMock.Setup(r => r.GetByIdAsync(specificCardId.Value, It.IsAny<CancellationToken>()))
                .ReturnsAsync(card);
        }
        else
        {
            // Setup for any card in the proposal operations
            foreach (var op in proposal.Operations)
            {
                if (op.TargetType.Equals("card", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrEmpty(op.TargetId)
                    && Guid.TryParse(op.TargetId, out var cardId))
                {
                    var card = CreateCard(cardId);
                    _cardRepoMock.Setup(r => r.GetByIdAsync(cardId, It.IsAny<CancellationToken>()))
                        .ReturnsAsync(card);
                }
            }
        }
    }

    private static void AddCardsToColumn(Column column, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var card = new Card(column.BoardId, column.Id, $"Card {i}", position: i);
            column.AddCard(card);
        }
    }

    #endregion
}
