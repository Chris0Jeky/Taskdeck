using FluentAssertions;
using Moq;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class SideEffectAnalyzerTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IAutomationProposalRepository> _proposalRepoMock;
    private readonly Mock<IOutboundWebhookSubscriptionRepository> _webhookRepoMock;
    private readonly SideEffectAnalyzer _analyzer;

    public SideEffectAnalyzerTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _proposalRepoMock = new Mock<IAutomationProposalRepository>();
        _webhookRepoMock = new Mock<IOutboundWebhookSubscriptionRepository>();

        _unitOfWorkMock.Setup(u => u.AutomationProposals).Returns(_proposalRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.OutboundWebhookSubscriptions).Returns(_webhookRepoMock.Object);

        _analyzer = new SideEffectAnalyzer(_unitOfWorkMock.Object);
    }

    private static AutomationProposal CreateProposal(
        RiskLevel riskLevel = RiskLevel.Low,
        Guid? boardId = null,
        params (string actionType, string targetType)[] operations)
    {
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            Guid.NewGuid(),
            "Test proposal",
            riskLevel,
            Guid.NewGuid().ToString(),
            boardId: boardId);

        for (int i = 0; i < operations.Length; i++)
        {
            proposal.AddOperation(new AutomationProposalOperation(
                proposal.Id,
                i,
                operations[i].actionType,
                operations[i].targetType,
                "{}",
                Guid.NewGuid().ToString()));
        }

        return proposal;
    }

    #region AnalyzeAsync Tests

    [Fact]
    public async Task AnalyzeAsync_ShouldReturnNotFound_WhenProposalDoesNotExist()
    {
        _proposalRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync((AutomationProposal?)null);

        var result = await _analyzer.AnalyzeAsync(Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NotFound");
    }

    [Fact]
    public async Task AnalyzeAsync_ShouldReturnSevenRows()
    {
        var proposal = CreateProposal(RiskLevel.Low, null, ("create", "card"));
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        var result = await _analyzer.AnalyzeAsync(proposal.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Rows.Should().HaveCount(7);
    }

    [Fact]
    public async Task AnalyzeAsync_ShouldReturnCorrectRowKeys()
    {
        var proposal = CreateProposal(RiskLevel.Low, null, ("create", "card"));
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        var result = await _analyzer.AnalyzeAsync(proposal.Id);

        result.IsSuccess.Should().BeTrue();
        var keys = result.Value.Rows.Select(r => r.Key).ToList();
        keys.Should().ContainInOrder("Cards", "Subtasks", "Comments", "Activity log", "Notifications", "Webhooks", "Calendar");
    }

    [Fact]
    public async Task AnalyzeAsync_CardsMutation_ShouldBeActive_WhenCreateOperation()
    {
        var proposal = CreateProposal(RiskLevel.Low, null, ("create", "card"));
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        var result = await _analyzer.AnalyzeAsync(proposal.Id);

        result.IsSuccess.Should().BeTrue();
        var cardsRow = result.Value.Rows.First(r => r.Key == "Cards");
        cardsRow.Tone.Should().Be("active");
    }

    [Fact]
    public async Task AnalyzeAsync_CardsMutation_ShouldBeActive_WhenMoveOperation()
    {
        var proposal = CreateProposal(RiskLevel.Low, null, ("move", "card"));
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        var result = await _analyzer.AnalyzeAsync(proposal.Id);

        var cardsRow = result.Value.Rows.First(r => r.Key == "Cards");
        cardsRow.Tone.Should().Be("active");
    }

    [Fact]
    public async Task AnalyzeAsync_CardsMutation_ShouldBeActive_WhenArchiveOperation()
    {
        var proposal = CreateProposal(RiskLevel.Low, null, ("archive", "card"));
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        var result = await _analyzer.AnalyzeAsync(proposal.Id);

        var cardsRow = result.Value.Rows.First(r => r.Key == "Cards");
        cardsRow.Tone.Should().Be("active");
    }

    [Fact]
    public async Task AnalyzeAsync_CardsMutation_ShouldBeActive_WhenBulkMoveOperation()
    {
        var proposal = CreateProposal(RiskLevel.Low, null, ("bulk_move", "card"));
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        var result = await _analyzer.AnalyzeAsync(proposal.Id);

        var cardsRow = result.Value.Rows.First(r => r.Key == "Cards");
        cardsRow.Tone.Should().Be("active");
    }

    [Fact]
    public async Task AnalyzeAsync_CardsMutation_ShouldBeActive_WhenCreateColumnOnly()
    {
        // Real column creation uses actionType "create" with targetType "column"
        var proposal = CreateProposal(RiskLevel.Low, null, ("create", "column"));
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        var result = await _analyzer.AnalyzeAsync(proposal.Id);

        var cardsRow = result.Value.Rows.First(r => r.Key == "Cards");
        cardsRow.Tone.Should().Be("active");
        cardsRow.Value.Should().Contain("columns");
    }

    [Fact]
    public async Task AnalyzeAsync_Subtasks_ShouldAlwaysBePassive()
    {
        var proposal = CreateProposal(RiskLevel.Low, null, ("create", "card"), ("move", "card"));
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        var result = await _analyzer.AnalyzeAsync(proposal.Id);

        var subtasksRow = result.Value.Rows.First(r => r.Key == "Subtasks");
        subtasksRow.Tone.Should().Be("passive");
    }

    [Fact]
    public async Task AnalyzeAsync_Comments_ShouldAlwaysBePassive()
    {
        var proposal = CreateProposal(RiskLevel.Low, null, ("create", "card"));
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        var result = await _analyzer.AnalyzeAsync(proposal.Id);

        var commentsRow = result.Value.Rows.First(r => r.Key == "Comments");
        commentsRow.Tone.Should().Be("passive");
    }

    [Fact]
    public async Task AnalyzeAsync_ActivityLog_ShouldBeActive_WhenOperationsExist()
    {
        var proposal = CreateProposal(RiskLevel.Low, null, ("create", "card"));
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        var result = await _analyzer.AnalyzeAsync(proposal.Id);

        var activityRow = result.Value.Rows.First(r => r.Key == "Activity log");
        activityRow.Tone.Should().Be("active");
    }

    [Fact]
    public async Task AnalyzeAsync_ActivityLog_ShouldBePassive_WhenNoOperations()
    {
        var proposal = CreateProposal(RiskLevel.Low, null);
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        var result = await _analyzer.AnalyzeAsync(proposal.Id);

        var activityRow = result.Value.Rows.First(r => r.Key == "Activity log");
        activityRow.Tone.Should().Be("passive");
    }

    [Fact]
    public async Task AnalyzeAsync_Notifications_ShouldBeActive_WhenOperationsExist()
    {
        var proposal = CreateProposal(RiskLevel.Low, null, ("move", "card"));
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        var result = await _analyzer.AnalyzeAsync(proposal.Id);

        var notifRow = result.Value.Rows.First(r => r.Key == "Notifications");
        notifRow.Tone.Should().Be("active");
    }

    [Fact]
    public async Task AnalyzeAsync_Notifications_ShouldBePassive_WhenNoOperations()
    {
        var proposal = CreateProposal(RiskLevel.Low, null);
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        var result = await _analyzer.AnalyzeAsync(proposal.Id);

        var notifRow = result.Value.Rows.First(r => r.Key == "Notifications");
        notifRow.Tone.Should().Be("passive");
    }

    [Fact]
    public async Task AnalyzeAsync_Webhooks_ShouldBeActive_WhenBoardHasActiveSubscriptions()
    {
        var boardId = Guid.NewGuid();
        var proposal = CreateProposal(RiskLevel.Low, boardId, ("create", "card"));
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        var subscription = new OutboundWebhookSubscription(boardId, Guid.NewGuid(), "https://example.com/webhook", "secret-key-123");
        _webhookRepoMock.Setup(r => r.GetActiveByBoardAsync(boardId, default))
            .ReturnsAsync(new List<OutboundWebhookSubscription> { subscription });

        var result = await _analyzer.AnalyzeAsync(proposal.Id);

        var webhookRow = result.Value.Rows.First(r => r.Key == "Webhooks");
        webhookRow.Tone.Should().Be("active");
    }

    [Fact]
    public async Task AnalyzeAsync_Webhooks_ShouldBePassive_WhenBoardHasNoSubscriptions()
    {
        var boardId = Guid.NewGuid();
        var proposal = CreateProposal(RiskLevel.Low, boardId, ("create", "card"));
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        _webhookRepoMock.Setup(r => r.GetActiveByBoardAsync(boardId, default))
            .ReturnsAsync(new List<OutboundWebhookSubscription>());

        var result = await _analyzer.AnalyzeAsync(proposal.Id);

        var webhookRow = result.Value.Rows.First(r => r.Key == "Webhooks");
        webhookRow.Tone.Should().Be("passive");
    }

    [Fact]
    public async Task AnalyzeAsync_Webhooks_ShouldBePassive_WhenNoBoardId()
    {
        var proposal = CreateProposal(RiskLevel.Low, null, ("create", "card"));
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        var result = await _analyzer.AnalyzeAsync(proposal.Id);

        var webhookRow = result.Value.Rows.First(r => r.Key == "Webhooks");
        webhookRow.Tone.Should().Be("passive");
    }

    [Fact]
    public async Task AnalyzeAsync_Calendar_ShouldAlwaysBePassive()
    {
        var proposal = CreateProposal(RiskLevel.Low, null, ("create", "card"));
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        var result = await _analyzer.AnalyzeAsync(proposal.Id);

        var calendarRow = result.Value.Rows.First(r => r.Key == "Calendar");
        calendarRow.Tone.Should().Be("passive");
    }

    [Fact]
    public async Task AnalyzeAsync_CardsMutation_ShouldNotTreatColumnCreateAsCardMutation()
    {
        // A "create" operation targeting "column" should NOT be classified as a card mutation
        var proposal = CreateProposal(RiskLevel.Low, null, ("create", "column"));
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        var result = await _analyzer.AnalyzeAsync(proposal.Id);

        var cardsRow = result.Value.Rows.First(r => r.Key == "Cards");
        // Should be active (column mutation) but description should mention columns, not card mutations
        cardsRow.Value.Should().NotContain("Creates, moves, or archives cards on the board");
        cardsRow.Value.Should().Contain("column");
    }

    [Fact]
    public async Task AnalyzeAsync_Cards_ShouldShowBothCardAndColumnMutations()
    {
        var proposal = CreateProposal(RiskLevel.Low, null, ("create", "card"), ("create", "column"));
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        var result = await _analyzer.AnalyzeAsync(proposal.Id);

        var cardsRow = result.Value.Rows.First(r => r.Key == "Cards");
        cardsRow.Tone.Should().Be("active");
        cardsRow.Value.Should().Contain("cards");
        cardsRow.Value.Should().Contain("columns");
    }

    [Fact]
    public async Task AnalyzeAsync_Webhooks_ShouldBePassive_WhenActiveWebhooksButNoOperations()
    {
        var boardId = Guid.NewGuid();
        var proposal = CreateProposal(RiskLevel.Low, boardId);
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        var subscription = new OutboundWebhookSubscription(boardId, Guid.NewGuid(), "https://example.com/webhook", "secret-key-123");
        _webhookRepoMock.Setup(r => r.GetActiveByBoardAsync(boardId, default))
            .ReturnsAsync(new List<OutboundWebhookSubscription> { subscription });

        var result = await _analyzer.AnalyzeAsync(proposal.Id);

        var webhookRow = result.Value.Rows.First(r => r.Key == "Webhooks");
        webhookRow.Tone.Should().Be("passive");
        webhookRow.Value.Should().Contain("no operations");
    }

    #endregion

    #region Reversibility Tests

    [Fact]
    public async Task AnalyzeAsync_Reversibility_ShouldUseDefaultWindow_ForLowRisk()
    {
        var proposal = CreateProposal(RiskLevel.Low, null, ("create", "card"));
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        var result = await _analyzer.AnalyzeAsync(proposal.Id);

        result.Value.Reversibility.WindowMs.Should().Be(Reversibility.DefaultWindowMs);
    }

    [Fact]
    public async Task AnalyzeAsync_Reversibility_ShouldUseDefaultWindow_ForMediumRisk()
    {
        var proposal = CreateProposal(RiskLevel.Medium, null, ("move", "card"));
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        var result = await _analyzer.AnalyzeAsync(proposal.Id);

        result.Value.Reversibility.WindowMs.Should().Be(Reversibility.DefaultWindowMs);
    }

    [Fact]
    public async Task AnalyzeAsync_Reversibility_ShouldUseDefaultWindow_ForHighRisk()
    {
        var proposal = CreateProposal(RiskLevel.High, null, ("archive", "card"));
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        var result = await _analyzer.AnalyzeAsync(proposal.Id);

        result.Value.Reversibility.WindowMs.Should().Be(Reversibility.DefaultWindowMs);
    }

    [Fact]
    public async Task AnalyzeAsync_Reversibility_ShouldUseHalfWindow_ForCriticalRisk()
    {
        var proposal = CreateProposal(RiskLevel.Critical, null, ("delete", "card"));
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        var result = await _analyzer.AnalyzeAsync(proposal.Id);

        result.Value.Reversibility.WindowMs.Should().Be(Reversibility.DefaultWindowMs / 2);
    }

    [Fact]
    public async Task AnalyzeAsync_Reversibility_ShouldDescribeNoOps_WhenNoOperations()
    {
        var proposal = CreateProposal(RiskLevel.Low, null);
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        var result = await _analyzer.AnalyzeAsync(proposal.Id);

        result.Value.Reversibility.Summary.Should().Contain("no operations");
        result.Value.Reversibility.WindowMs.Should().Be(Reversibility.DefaultWindowMs);
    }

    [Fact]
    public async Task AnalyzeAsync_Reversibility_CriticalRisk_ShouldMentionManualIntervention()
    {
        var proposal = CreateProposal(RiskLevel.Critical, null, ("delete", "card"));
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        var result = await _analyzer.AnalyzeAsync(proposal.Id);

        result.Value.Reversibility.Summary.Should().Contain("manual intervention");
        result.Value.Reversibility.Description.Should().Contain("Critical-risk");
    }

    #endregion

    #region BuildSideEffectRows Static Tests

    [Fact]
    public void BuildSideEffectRows_ShouldAlwaysReturn7Rows()
    {
        var operations = new List<AutomationProposalOperation>();
        var rows = SideEffectAnalyzer.BuildSideEffectRows(operations, false);

        rows.Should().HaveCount(7);
    }

    [Fact]
    public void BuildSideEffectRows_EmptyOperations_ShouldHaveAllPassiveExceptNone()
    {
        var operations = new List<AutomationProposalOperation>();
        var rows = SideEffectAnalyzer.BuildSideEffectRows(operations, false);

        // Cards, Subtasks, Comments, Activity log, Notifications, Webhooks, Calendar all passive
        rows.Should().OnlyContain(r => r.Tone == SideEffectTone.Passive);
    }

    [Fact]
    public void BuildSideEffectRows_WithCardCreate_ShouldSetCardsActive()
    {
        var op = new AutomationProposalOperation(
            Guid.NewGuid(), 0, "create", "card", "{}", Guid.NewGuid().ToString());
        var rows = SideEffectAnalyzer.BuildSideEffectRows(new List<AutomationProposalOperation> { op }, false);

        var cardsRow = rows.First(r => r.Key == "Cards");
        cardsRow.Tone.Should().Be(SideEffectTone.Active);
    }

    [Fact]
    public void BuildSideEffectRows_WithWebhooks_ShouldSetWebhooksActive()
    {
        var op = new AutomationProposalOperation(
            Guid.NewGuid(), 0, "create", "card", "{}", Guid.NewGuid().ToString());
        var rows = SideEffectAnalyzer.BuildSideEffectRows(new List<AutomationProposalOperation> { op }, true);

        var webhookRow = rows.First(r => r.Key == "Webhooks");
        webhookRow.Tone.Should().Be(SideEffectTone.Active);
    }

    [Fact]
    public void BuildSideEffectRows_CreateColumnOperation_ShouldSetCardsActive()
    {
        var op = new AutomationProposalOperation(
            Guid.NewGuid(), 0, "create", "column", "{}", Guid.NewGuid().ToString());
        var rows = SideEffectAnalyzer.BuildSideEffectRows(new List<AutomationProposalOperation> { op }, false);

        var cardsRow = rows.First(r => r.Key == "Cards");
        cardsRow.Tone.Should().Be(SideEffectTone.Active);
        cardsRow.Value.Should().Contain("column");
    }

    [Fact]
    public void BuildSideEffectRows_CreateTargetingNonCard_ShouldNotSetCardMutation()
    {
        // "create" targeting "column" should not say "Creates, moves, or archives cards"
        var op = new AutomationProposalOperation(
            Guid.NewGuid(), 0, "create", "column", "{}", Guid.NewGuid().ToString());
        var rows = SideEffectAnalyzer.BuildSideEffectRows(new List<AutomationProposalOperation> { op }, false);

        var cardsRow = rows.First(r => r.Key == "Cards");
        cardsRow.Value.Should().NotBe("Creates, moves, or archives cards on the board");
    }

    [Fact]
    public void BuildSideEffectRows_WithWebhooksButNoOps_ShouldSetWebhooksPassive()
    {
        var operations = new List<AutomationProposalOperation>();
        var rows = SideEffectAnalyzer.BuildSideEffectRows(operations, hasActiveWebhooks: true);

        var webhookRow = rows.First(r => r.Key == "Webhooks");
        webhookRow.Tone.Should().Be(SideEffectTone.Passive);
        webhookRow.Value.Should().Contain("no operations");
    }

    #endregion

    #region ComputeReversibility Static Tests

    [Theory]
    [InlineData(RiskLevel.Low)]
    [InlineData(RiskLevel.Medium)]
    [InlineData(RiskLevel.High)]
    public void ComputeReversibility_NonCritical_ShouldUseDefaultWindow(RiskLevel level)
    {
        var op = new AutomationProposalOperation(
            Guid.NewGuid(), 0, "create", "card", "{}", Guid.NewGuid().ToString());
        var rev = SideEffectAnalyzer.ComputeReversibility(new List<AutomationProposalOperation> { op }, level);

        rev.WindowMs.Should().Be(Reversibility.DefaultWindowMs);
    }

    [Fact]
    public void ComputeReversibility_Critical_ShouldUseHalfWindow()
    {
        var op = new AutomationProposalOperation(
            Guid.NewGuid(), 0, "delete", "card", "{}", Guid.NewGuid().ToString());
        var rev = SideEffectAnalyzer.ComputeReversibility(new List<AutomationProposalOperation> { op }, RiskLevel.Critical);

        rev.WindowMs.Should().Be(Reversibility.DefaultWindowMs / 2);
    }

    [Fact]
    public void ComputeReversibility_NoOperations_ShouldDescribeNoOps()
    {
        var rev = SideEffectAnalyzer.ComputeReversibility(new List<AutomationProposalOperation>(), RiskLevel.Low);

        rev.Summary.Should().Contain("no operations");
        rev.Description.Should().Contain("no operations");
        rev.WindowMs.Should().Be(Reversibility.DefaultWindowMs);
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_ShouldThrow_WhenUnitOfWorkIsNull()
    {
        var act = () => new SideEffectAnalyzer(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    #endregion
}
