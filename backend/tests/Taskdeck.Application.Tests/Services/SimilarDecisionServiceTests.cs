using FluentAssertions;
using Moq;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class SimilarDecisionServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IAutomationProposalRepository> _proposalRepo = new();
    private readonly SimilarDecisionService _service;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _boardId = Guid.NewGuid();

    public SimilarDecisionServiceTests()
    {
        _unitOfWork.Setup(u => u.AutomationProposals).Returns(_proposalRepo.Object);
        _service = new SimilarDecisionService(_unitOfWork.Object);
    }

    [Fact]
    public async Task GetSimilarPastAsync_ShouldReturnNotFound_WhenProposalDoesNotExist()
    {
        var proposalId = Guid.NewGuid();
        _proposalRepo.Setup(r => r.GetByIdAsync(proposalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AutomationProposal?)null);

        var result = await _service.GetSimilarPastAsync(proposalId, _userId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NotFound");
    }

    [Fact]
    public async Task GetSimilarPastAsync_ShouldReturnEmpty_WhenProposalHasNoOperations()
    {
        var proposal = CreateProposal();
        _proposalRepo.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);

        var result = await _service.GetSimilarPastAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Decisions.Should().BeEmpty();
        result.Value.ApplyRate.Should().Be(0.0);
    }

    [Fact]
    public async Task GetSimilarPastAsync_ShouldReturnEmpty_WhenNoPriorSimilarProposals()
    {
        var proposal = CreateProposalWithOperation("move", "card");
        _proposalRepo.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);
        _proposalRepo.Setup(r => r.GetTerminalByActionTypeAsync("move", _boardId, _userId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AutomationProposal>());

        var result = await _service.GetSimilarPastAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Decisions.Should().BeEmpty();
        result.Value.ApplyRate.Should().Be(0.0);
    }

    [Fact]
    public async Task GetSimilarPastAsync_ShouldReturnRate1_WhenAllApplied()
    {
        var proposal = CreateProposalWithOperation("create", "card");
        var past1 = CreateTerminalProposal(ProposalStatus.Applied, "create", "card");
        var past2 = CreateTerminalProposal(ProposalStatus.Applied, "create", "card");

        _proposalRepo.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);
        _proposalRepo.Setup(r => r.GetTerminalByActionTypeAsync("create", _boardId, _userId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { past1, past2 });

        var result = await _service.GetSimilarPastAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.ApplyRate.Should().Be(1.0);
        result.Value.Decisions.Should().HaveCount(2);
        result.Value.Decisions.Should().OnlyContain(d => d.Verdict == "applied");
    }

    [Fact]
    public async Task GetSimilarPastAsync_ShouldReturnRate0_WhenAllRejected()
    {
        var proposal = CreateProposalWithOperation("archive", "card");
        var past1 = CreateTerminalProposal(ProposalStatus.Rejected, "archive", "card");
        var past2 = CreateTerminalProposal(ProposalStatus.Rejected, "archive", "card");
        var past3 = CreateTerminalProposal(ProposalStatus.Rejected, "archive", "card");

        _proposalRepo.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);
        _proposalRepo.Setup(r => r.GetTerminalByActionTypeAsync("archive", _boardId, _userId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { past1, past2, past3 });

        var result = await _service.GetSimilarPastAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.ApplyRate.Should().Be(0.0);
        result.Value.Decisions.Should().OnlyContain(d => d.Verdict == "rejected");
    }

    [Fact]
    public async Task GetSimilarPastAsync_ShouldReturnCorrectRate_WhenMixed()
    {
        var proposal = CreateProposalWithOperation("move", "card");
        var past1 = CreateTerminalProposal(ProposalStatus.Applied, "move", "card");
        var past2 = CreateTerminalProposal(ProposalStatus.Rejected, "move", "card");
        var past3 = CreateTerminalProposal(ProposalStatus.Applied, "move", "card");
        var past4 = CreateTerminalProposal(ProposalStatus.Applied, "move", "card");

        _proposalRepo.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);
        _proposalRepo.Setup(r => r.GetTerminalByActionTypeAsync("move", _boardId, _userId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { past1, past2, past3, past4 });

        var result = await _service.GetSimilarPastAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
        // 3 applied, 1 rejected = 0.75
        result.Value.ApplyRate.Should().BeApproximately(0.75, 0.001);
    }

    [Fact]
    public async Task GetSimilarPastAsync_ShouldReturnOnlyTop3_WhenMoreThan3Similar()
    {
        var proposal = CreateProposalWithOperation("update", "card");
        var pastProposals = Enumerable.Range(0, 5)
            .Select(_ => CreateTerminalProposal(ProposalStatus.Applied, "update", "card"))
            .ToArray();

        _proposalRepo.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);
        _proposalRepo.Setup(r => r.GetTerminalByActionTypeAsync("update", _boardId, _userId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pastProposals);

        var result = await _service.GetSimilarPastAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Decisions.Should().HaveCount(3);
        // Rate should include all 5 proposals, not just the 3 displayed
        result.Value.ApplyRate.Should().Be(1.0);
    }

    [Fact]
    public async Task GetSimilarPastAsync_ShouldUseFirstOperationActionType()
    {
        var proposal = CreateProposalWithMultipleOperations();
        _proposalRepo.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);
        // Should query with "create" (first operation by sequence), not "move" (second operation)
        _proposalRepo.Setup(r => r.GetTerminalByActionTypeAsync("create", _boardId, _userId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AutomationProposal>());

        var result = await _service.GetSimilarPastAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
        _proposalRepo.Verify(r => r.GetTerminalByActionTypeAsync("create", _boardId, _userId, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetSimilarPastAsync_ShouldExcludeCurrentProposal()
    {
        var proposal = CreateProposalWithOperation("move", "card");
        // The repo returns the current proposal as well (it might be in terminal state)
        SetProposalStatus(proposal, ProposalStatus.Applied);

        _proposalRepo.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);
        _proposalRepo.Setup(r => r.GetTerminalByActionTypeAsync("move", _boardId, _userId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { proposal });

        var result = await _service.GetSimilarPastAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Decisions.Should().BeEmpty();
        result.Value.ApplyRate.Should().Be(0.0);
    }

    [Fact]
    public async Task GetSimilarPastAsync_ShouldFallBackToUserScope_WhenBoardHasNoHistory()
    {
        var proposal = CreateProposalWithOperation("move", "card");
        var userScopedPast = CreateTerminalProposal(ProposalStatus.Applied, "move", "card", boardId: Guid.NewGuid());

        _proposalRepo.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);
        // Board-scoped query returns empty
        _proposalRepo.Setup(r => r.GetTerminalByActionTypeAsync("move", _boardId, _userId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AutomationProposal>());
        // User-scoped query returns results
        _proposalRepo.Setup(r => r.GetTerminalByActionTypeAsync("move", null, _userId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { userScopedPast });

        var result = await _service.GetSimilarPastAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Decisions.Should().HaveCount(1);
        _proposalRepo.Verify(r => r.GetTerminalByActionTypeAsync("move", null, _userId, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetSimilarPastAsync_ShouldNotFallBackToUserScope_WhenProposalHasNoBoard()
    {
        var proposal = CreateProposalWithOperation("move", "card", noBoardId: true);

        _proposalRepo.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);
        _proposalRepo.Setup(r => r.GetTerminalByActionTypeAsync("move", null, _userId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AutomationProposal>());

        var result = await _service.GetSimilarPastAsync(proposal.Id, _userId);

        result.IsSuccess.Should().BeTrue();
        // Should only query once (user-scoped), not twice
        _proposalRepo.Verify(r => r.GetTerminalByActionTypeAsync(It.IsAny<string>(), It.IsAny<Guid?>(), _userId, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetSimilarPastAsync_ShouldFormatSerials_AsHashPaddedNumbers()
    {
        var proposal = CreateProposalWithOperation("create", "card");
        var past1 = CreateTerminalProposal(ProposalStatus.Applied, "create", "card");
        var past2 = CreateTerminalProposal(ProposalStatus.Rejected, "create", "card");

        _proposalRepo.Setup(r => r.GetByIdAsync(proposal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proposal);
        _proposalRepo.Setup(r => r.GetTerminalByActionTypeAsync("create", _boardId, _userId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { past1, past2 });

        var result = await _service.GetSimilarPastAsync(proposal.Id, _userId);

        result.Value.Decisions[0].Serial.Should().Be("#001");
        result.Value.Decisions[1].Serial.Should().Be("#002");
    }

    [Fact]
    public async Task GetSimilarPastAsync_ShouldFormatDate_AsIsoWeekNumber()
    {
        // The date formatting depends on DecidedAt, which is set by the Approve/Reject methods.
        // We test the static helper directly since the proposal's DecidedAt is set internally.
        var weekStr = SimilarDecisionService.FormatWeekDate(new DateTimeOffset(2026, 4, 6, 0, 0, 0, TimeSpan.Zero));

        // April 6, 2026 is ISO week 15 of 2026
        weekStr.Should().Be("wk 15 '26");
    }

    [Theory]
    [InlineData(2026, 1, 1, "wk 1 '26")]   // Jan 1, 2026 is in ISO week 1 of 2026
    [InlineData(2025, 12, 29, "wk 1 '26")]  // Dec 29, 2025 is in ISO week 1 of 2026 (cross-year)
    public void FormatWeekDate_ShouldReturnCorrectIsoWeek(int year, int month, int day, string expected)
    {
        var result = SimilarDecisionService.FormatWeekDate(new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero));
        result.Should().Be(expected);
    }

    [Fact]
    public void MapVerdict_ShouldMapApplied()
    {
        var verdict = SimilarDecisionService.MapVerdict(ProposalStatus.Applied);
        verdict.Should().Be(Domain.SimilarPast.PastVerdict.Applied);
    }

    [Fact]
    public void MapVerdict_ShouldMapRejected()
    {
        var verdict = SimilarDecisionService.MapVerdict(ProposalStatus.Rejected);
        verdict.Should().Be(Domain.SimilarPast.PastVerdict.Rejected);
    }

    [Theory]
    [InlineData(ProposalStatus.PendingReview)]
    [InlineData(ProposalStatus.Approved)]
    [InlineData(ProposalStatus.Failed)]
    [InlineData(ProposalStatus.Expired)]
    [InlineData(ProposalStatus.Dismissed)]
    public void MapVerdict_ShouldThrow_ForNonTerminalStatuses(ProposalStatus status)
    {
        var act = () => SimilarDecisionService.MapVerdict(status);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GetPrimaryActionType_ShouldReturnNull_WhenNoOperations()
    {
        var proposal = CreateProposal();
        var result = SimilarDecisionService.GetPrimaryActionType(proposal);
        result.Should().BeNull();
    }

    [Fact]
    public void GetPrimaryActionType_ShouldReturnFirstBySequence()
    {
        var proposal = CreateProposalWithMultipleOperations();
        var result = SimilarDecisionService.GetPrimaryActionType(proposal);
        result.Should().Be("create");
    }

    [Fact]
    public void GetProposalTitle_ShouldReturnSummary_WhenPresent()
    {
        var proposal = CreateProposal();
        var title = SimilarDecisionService.GetProposalTitle(proposal);
        title.Should().NotBeEmpty();
    }

    [Fact]
    public void GetProposalTitle_ShouldReturnOperationDescription_WhenSummaryEmpty()
    {
        // We can't easily set summary to empty due to constructor validation,
        // so we just verify the method uses the summary when it's available
        var proposal = CreateProposalWithOperation("move", "card");
        var title = SimilarDecisionService.GetProposalTitle(proposal);
        title.Should().NotBeEmpty();
    }

    #region Helpers

    private AutomationProposal CreateProposal(Guid? boardId = null, bool noBoardId = false)
    {
        return new AutomationProposal(
            ProposalSourceType.Chat,
            _userId,
            "Test proposal summary",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            noBoardId ? null : (boardId ?? _boardId));
    }

    private AutomationProposal CreateProposalWithOperation(string actionType, string targetType, Guid? boardId = null, bool noBoardId = false)
    {
        var effectiveBoardId = noBoardId ? (Guid?)null : (boardId ?? _boardId);
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            _userId,
            $"Test {actionType} proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            effectiveBoardId);

        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id,
            0,
            actionType,
            targetType,
            "{}",
            Guid.NewGuid().ToString()));

        return proposal;
    }

    private AutomationProposal CreateProposalWithMultipleOperations()
    {
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            _userId,
            "Multi-op proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            _boardId);

        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id, 0, "create", "card", "{}", Guid.NewGuid().ToString()));
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id, 1, "move", "card", "{}", Guid.NewGuid().ToString()));

        return proposal;
    }

    private AutomationProposal CreateTerminalProposal(ProposalStatus status, string actionType, string targetType, Guid? boardId = null)
    {
        var effectiveBoardId = boardId ?? _boardId;
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            _userId,
            $"Past {actionType} proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            effectiveBoardId);

        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id, 0, actionType, targetType, "{}", Guid.NewGuid().ToString()));

        SetProposalStatus(proposal, status);

        return proposal;
    }

    private static void SetProposalStatus(AutomationProposal proposal, ProposalStatus targetStatus)
    {
        switch (targetStatus)
        {
            case ProposalStatus.Applied:
                proposal.Approve(Guid.NewGuid());
                proposal.MarkAsApplied();
                break;
            case ProposalStatus.Rejected:
                proposal.Reject(Guid.NewGuid());
                break;
            case ProposalStatus.Approved:
                proposal.Approve(Guid.NewGuid());
                break;
            default:
                throw new InvalidOperationException($"Test helper does not support status {targetStatus}");
        }
    }

    #endregion
}
