using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
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
    public async Task AnalyzeAsync_UsesEffectiveOperationSnapshot()
    {
        var effectiveProposal = new ProposalDto(
            Guid.NewGuid(), ProposalSourceType.Chat, null, null, Guid.NewGuid(), ProposalStatus.PendingReview,
            RiskLevel.Low, "Effective proposal", null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            DateTime.UtcNow.AddDays(1), null, null, null, null, Guid.NewGuid().ToString(),
            new List<ProposalOperationDto>
            {
                new(Guid.NewGuid(), Guid.NewGuid(), 0, "create", "card", null, "{}", Guid.NewGuid().ToString(), null)
            });

        var result = await _analyzer.AnalyzeAsync(effectiveProposal);

        result.IsSuccess.Should().BeTrue();
        result.Value.Rows.Single(row => row.Key == "Cards").Tone.Should().Be("active");
        result.Value.Rows.Single(row => row.Key == "Cards").Value.Should().Be("Creates cards on the board");
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

    [Theory]
    [InlineData("archive-lifecycle")]
    [InlineData("restore-lifecycle")]
    [InlineData("ARCHIVE-LIFECYCLE")]
    [InlineData("Restore-Lifecycle")]
    public async Task AnalyzeAsync_CardsMutation_ShouldBeActive_WhenCardLifecycleOperation(string actionType)
    {
        // #2939: applying either lifecycle action flips the card's archived state, so the review
        // disclosure must not claim "No board mutations" for the write being approved.
        var proposal = CreateProposal(RiskLevel.Low, null, (actionType, "card"));
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        var result = await _analyzer.AnalyzeAsync(proposal.Id);

        var cardsRow = result.Value.Rows.First(r => r.Key == "Cards");
        cardsRow.Tone.Should().Be("active");
        cardsRow.Value.Should().NotBe("No board mutations");
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
        cardsRow.Value.Should().Be("Adds columns to the board (no direct card mutations)");
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
        cardsRow.Value.Should().Be("Creates cards and adds columns on the board");
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

    #region Apply Risk Posture Tests

    [Fact]
    public async Task AnalyzeAsync_ApplyRisk_ShouldKeepCompatibilityWindow_ForLowRisk()
    {
        var proposal = CreateProposal(RiskLevel.Low, null, ("create", "card"));
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        var result = await _analyzer.AnalyzeAsync(proposal.Id);

        result.Value.Reversibility.WindowMs.Should().Be(Reversibility.DefaultWindowMs);
    }

    [Fact]
    public async Task AnalyzeAsync_ApplyRisk_ShouldKeepCompatibilityWindow_ForMediumRisk()
    {
        var proposal = CreateProposal(RiskLevel.Medium, null, ("move", "card"));
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        var result = await _analyzer.AnalyzeAsync(proposal.Id);

        result.Value.Reversibility.WindowMs.Should().Be(Reversibility.DefaultWindowMs);
    }

    [Fact]
    public async Task AnalyzeAsync_ApplyRisk_ShouldKeepCompatibilityWindow_ForHighRisk()
    {
        var proposal = CreateProposal(RiskLevel.High, null, ("archive", "card"));
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        var result = await _analyzer.AnalyzeAsync(proposal.Id);

        result.Value.Reversibility.WindowMs.Should().Be(Reversibility.DefaultWindowMs);
    }

    [Fact]
    public async Task AnalyzeAsync_ApplyRisk_ShouldKeepTighterAttentionMetadata_ForCriticalRisk()
    {
        var proposal = CreateProposal(RiskLevel.Critical, null, ("delete", "card"));
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        var result = await _analyzer.AnalyzeAsync(proposal.Id);

        result.Value.Reversibility.WindowMs.Should().Be(Reversibility.DefaultWindowMs / 2);
    }

    [Fact]
    public async Task AnalyzeAsync_ApplyRisk_ShouldDescribeNoOps_WhenNoOperations()
    {
        var proposal = CreateProposal(RiskLevel.Low, null);
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        var result = await _analyzer.AnalyzeAsync(proposal.Id);

        result.Value.Reversibility.Summary.Should().Be("No operations to apply");
        result.Value.Reversibility.WindowMs.Should().Be(Reversibility.DefaultWindowMs);
    }

    [Fact]
    public async Task AnalyzeAsync_ApplyRisk_CriticalRisk_ShouldMentionManualRecovery()
    {
        var proposal = CreateProposal(RiskLevel.Critical, null, ("delete", "card"));
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        var result = await _analyzer.AnalyzeAsync(proposal.Id);

        result.Value.Reversibility.Summary.Should().Contain("manual recovery");
        result.Value.Reversibility.Description.Should().Contain("Critical-risk");
    }

    [Theory]
    [InlineData(RiskLevel.Low)]
    [InlineData(RiskLevel.Medium)]
    [InlineData(RiskLevel.High)]
    [InlineData(RiskLevel.Critical)]
    public async Task AnalyzeAsync_ApplyRisk_ShouldNotPromiseUndo(RiskLevel riskLevel)
    {
        var proposal = CreateProposal(riskLevel, null, ("create", "card"));
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposal.Id, default))
            .ReturnsAsync(proposal);

        var result = await _analyzer.AnalyzeAsync(proposal.Id);

        var copy = $"{result.Value.Reversibility.Summary} {result.Value.Reversibility.Description}"
            .ToLowerInvariant();
        copy.Should().NotContain("undo");
        copy.Should().NotContain("revers");
        copy.Should().NotContain("single keystroke");
    }

    #endregion

    #region BuildSideEffectRows Static Tests

    [Theory]
    [InlineData("create", "Creates")]
    [InlineData("move", "Moves")]
    [InlineData("bulk_move", "Moves")]
    [InlineData("update", "Updates")]
    [InlineData("delete", "Deletes")]
    [InlineData("archive", "Blocks")]
    [InlineData("archive-lifecycle", "Archives")]
    [InlineData("restore-lifecycle", "Restores")]
    [InlineData("DELETE", "Deletes")]
    public void BuildSideEffectRows_SingleCardAction_DisclosesOnlyItsActualEffect(string action, string verb)
    {
        var proposal = CreateProposal(RiskLevel.Low, null, (action, "card"));

        var rows = SideEffectAnalyzer.BuildSideEffectRows(proposal.Operations, false);

        var cards = rows.Single(row => row.Key == "Cards");
        cards.Value.Should().Be($"{verb} cards on the board");
        cards.Tone.Should().Be(SideEffectTone.Active);
    }

    [Theory]
    [InlineData(new string[] { "delete", "archive-lifecycle" }, "Deletes and archives cards on the board")]
    [InlineData(new string[] { "delete", "archive" }, "Blocks and deletes cards on the board")]
    [InlineData(new string[] { "delete", "delete", "DELETE" }, "Deletes cards on the board")]
    [InlineData(new string[] { "bulk_move", "move", "MOVE" }, "Moves cards on the board")]
    [InlineData(new string[] { "delete", "create", "update", "create" }, "Creates, updates, and deletes cards on the board")]
    [InlineData(new string[] { "restore-lifecycle", "archive-lifecycle", "delete", "update", "archive", "bulk_move", "move", "create" },
        "Creates, moves, blocks, updates, deletes, archives, and restores cards on the board")]
    public void BuildSideEffectRows_MixedCardActions_DisclosesEachEffectOnceInStableOrder(string[] actions, string expected)
    {
        var proposal = CreateProposal(RiskLevel.Low, null, actions.Select(action => (action, "card")).ToArray());

        var rows = SideEffectAnalyzer.BuildSideEffectRows(proposal.Operations, false);

        var cards = rows.Single(row => row.Key == "Cards");
        cards.Value.Should().Be(expected);
        cards.Tone.Should().Be(SideEffectTone.Active);
    }

    [Fact]
    public void BuildSideEffectRows_DeleteCardWithOtherTargets_DescribesOnlyCardDeletionAndColumnCreation()
    {
        var proposal = CreateProposal(RiskLevel.Low, null,
            ("delete", "CARD"), ("create", "column"), ("restore-lifecycle", "artefact"), ("update", "board"));

        var rows = SideEffectAnalyzer.BuildSideEffectRows(proposal.Operations, false);

        rows.Single(row => row.Key == "Cards").Value.Should().Be("Deletes cards and adds columns on the board");
    }

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

    [Theory]
    [InlineData("archive-lifecycle")]
    [InlineData("restore-lifecycle")]
    public void BuildSideEffectRows_WithCardLifecycleAction_ShouldSetCardsActive(string actionType)
    {
        var op = new AutomationProposalOperation(
            Guid.NewGuid(), 0, actionType, "card", "{}", Guid.NewGuid().ToString());
        var rows = SideEffectAnalyzer.BuildSideEffectRows(new List<AutomationProposalOperation> { op }, false);

        var cardsRow = rows.First(r => r.Key == "Cards");
        cardsRow.Tone.Should().Be(SideEffectTone.Active);
        cardsRow.Value.Should().NotBe("No board mutations");
    }

    [Fact]
    public void BuildSideEffectRows_WithCardLifecycleActionTargetingNonCard_ShouldNotSetCardMutation()
    {
        // Guards the pairing: the action set only counts when the target really is a card.
        var op = new AutomationProposalOperation(
            Guid.NewGuid(), 0, "archive-lifecycle", "board", "{}", Guid.NewGuid().ToString());
        var rows = SideEffectAnalyzer.BuildSideEffectRows(new List<AutomationProposalOperation> { op }, false);

        var cardsRow = rows.First(r => r.Key == "Cards");
        cardsRow.Value.Should().Be("No board mutations");
        cardsRow.Tone.Should().Be(SideEffectTone.Passive);
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
        // A column creation must not imply that cards will be created.
        var op = new AutomationProposalOperation(
            Guid.NewGuid(), 0, "create", "column", "{}", Guid.NewGuid().ToString());
        var rows = SideEffectAnalyzer.BuildSideEffectRows(new List<AutomationProposalOperation> { op }, false);

        var cardsRow = rows.First(r => r.Key == "Cards");
        cardsRow.Value.Should().Be("Adds columns to the board (no direct card mutations)");
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

    #region ComputeApplyRiskPosture Static Tests

    [Theory]
    [InlineData(RiskLevel.Low)]
    [InlineData(RiskLevel.Medium)]
    [InlineData(RiskLevel.High)]
    public void ComputeApplyRiskPosture_NonCritical_ShouldKeepCompatibilityWindow(RiskLevel level)
    {
        var op = new AutomationProposalOperation(
            Guid.NewGuid(), 0, "create", "card", "{}", Guid.NewGuid().ToString());
        var rev = SideEffectAnalyzer.ComputeApplyRiskPosture(new List<AutomationProposalOperation> { op }, level);

        rev.WindowMs.Should().Be(Reversibility.DefaultWindowMs);
    }

    [Fact]
    public void ComputeApplyRiskPosture_Critical_ShouldKeepTighterAttentionMetadata()
    {
        var op = new AutomationProposalOperation(
            Guid.NewGuid(), 0, "delete", "card", "{}", Guid.NewGuid().ToString());
        var rev = SideEffectAnalyzer.ComputeApplyRiskPosture(new List<AutomationProposalOperation> { op }, RiskLevel.Critical);

        rev.WindowMs.Should().Be(Reversibility.DefaultWindowMs / 2);
    }

    [Fact]
    public void ComputeApplyRiskPosture_NoOperations_ShouldDescribeNoOps()
    {
        var rev = SideEffectAnalyzer.ComputeApplyRiskPosture(new List<AutomationProposalOperation>(), RiskLevel.Low);

        rev.Summary.Should().Be("No operations to apply");
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
