using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class WorkspaceServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IUserPreferenceRepository> _userPreferenceRepositoryMock = new();
    private readonly Mock<IBoardRepository> _boardRepositoryMock = new();
    private readonly Mock<ICardRepository> _cardRepositoryMock = new();
    private readonly Mock<ILlmQueueRepository> _llmQueueRepositoryMock = new();
    private readonly Mock<IAutomationProposalRepository> _proposalRepositoryMock = new();
    private readonly WorkspaceService _service;

    public WorkspaceServiceTests()
    {
        _unitOfWorkMock.SetupGet(unitOfWork => unitOfWork.UserPreferences).Returns(_userPreferenceRepositoryMock.Object);
        _unitOfWorkMock.SetupGet(unitOfWork => unitOfWork.Boards).Returns(_boardRepositoryMock.Object);
        _unitOfWorkMock.SetupGet(unitOfWork => unitOfWork.Cards).Returns(_cardRepositoryMock.Object);
        _unitOfWorkMock.SetupGet(unitOfWork => unitOfWork.LlmQueue).Returns(_llmQueueRepositoryMock.Object);
        _unitOfWorkMock.SetupGet(unitOfWork => unitOfWork.AutomationProposals).Returns(_proposalRepositoryMock.Object);
        _unitOfWorkMock.Setup(unitOfWork => unitOfWork.SaveChangesAsync(default)).ReturnsAsync(1);

        _userPreferenceRepositoryMock
            .Setup(repository => repository.AddAsync(It.IsAny<UserPreference>(), default))
            .ReturnsAsync((UserPreference preference, CancellationToken _) => preference);

        _proposalRepositoryMock
            .Setup(repository => repository.GetByUserIdAsync(It.IsAny<Guid>(), int.MaxValue, default))
            .ReturnsAsync([]);
        _proposalRepositoryMock
            .Setup(repository => repository.CountPendingReviewByUserIdAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync(0);
        _proposalRepositoryMock
            .Setup(repository => repository.HasReviewedByUserIdAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync(false);

        _llmQueueRepositoryMock
            .Setup(repository => repository.GetByUserAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync([]);
        _llmQueueRepositoryMock
            .Setup(repository => repository.GetCaptureSummaryByUserAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync((0, 0, 0, 0, 0));

        _boardRepositoryMock
            .Setup(repository => repository.CountReadableByUserIdAsync(It.IsAny<Guid>(), false, default))
            .ReturnsAsync(0);
        _boardRepositoryMock
            .Setup(repository => repository.CountReadableUpdatedSinceAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), false, default))
            .ReturnsAsync(0);
        _boardRepositoryMock
            .Setup(repository => repository.GetRecentReadableByUserIdAsync(It.IsAny<Guid>(), It.IsAny<int>(), false, default))
            .ReturnsAsync([]);
        _boardRepositoryMock
            .Setup(repository => repository.GetReadableByUserIdAsync(It.IsAny<Guid>(), false, default))
            .ReturnsAsync([]);

        _cardRepositoryMock
            .Setup(repository => repository.GetByBoardIdsAsync(It.IsAny<IEnumerable<Guid>>(), default))
            .ReturnsAsync([]);
        _cardRepositoryMock
            .Setup(repository => repository.GetAgendaByBoardIdsAsync(It.IsAny<IEnumerable<Guid>>(), default))
            .ReturnsAsync([]);

        _service = new WorkspaceService(_unitOfWorkMock.Object);
    }

    [Fact]
    public async Task GetPreferencesAsync_ShouldCreateDefaultGuidedPreference_WhenNoneExists()
    {
        var userId = Guid.NewGuid();
        _userPreferenceRepositoryMock
            .Setup(repository => repository.GetByUserIdAsync(userId, default))
            .ReturnsAsync((UserPreference?)null);

        var result = await _service.GetPreferencesAsync(userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.WorkspaceMode.Should().Be(WorkspaceModeContract.Guided);
        result.Value.Onboarding.Visibility.Should().Be(WorkspaceOnboardingVisibilityContract.Active);
        result.Value.Onboarding.IsComplete.Should().BeFalse();
        _userPreferenceRepositoryMock.Verify(repository => repository.AddAsync(It.IsAny<UserPreference>(), default), Times.Once);
        _unitOfWorkMock.Verify(unitOfWork => unitOfWork.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task GetPreferencesAsync_ShouldUseLightweightOnboardingQueries()
    {
        var userId = Guid.NewGuid();
        var preference = new UserPreference(userId, WorkspaceMode.Guided);

        _userPreferenceRepositoryMock
            .Setup(repository => repository.GetByUserIdAsync(userId, default))
            .ReturnsAsync(preference);
        _boardRepositoryMock
            .Setup(repository => repository.CountReadableByUserIdAsync(userId, false, default))
            .ReturnsAsync(1);
        _llmQueueRepositoryMock
            .Setup(repository => repository.GetCaptureSummaryByUserAsync(userId, default))
            .ReturnsAsync((1, 1, 0, 0, 0));

        var result = await _service.GetPreferencesAsync(userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be(userId);
        _proposalRepositoryMock.Verify(repository => repository.GetByUserIdAsync(userId, int.MaxValue, default), Times.Never);
        _boardRepositoryMock.Verify(repository => repository.GetReadableByUserIdAsync(userId, false, default), Times.Never);
        _cardRepositoryMock.Verify(repository => repository.GetAgendaByBoardIdsAsync(It.IsAny<IEnumerable<Guid>>(), default), Times.Never);
        _llmQueueRepositoryMock.Verify(repository => repository.GetByUserAsync(userId, default), Times.Never);
    }

    [Fact]
    public async Task GetPreferencesAsync_ShouldReturnPersistedPreference_WhenConcurrentCreateWins()
    {
        var userId = Guid.NewGuid();
        var persistedPreference = new UserPreference(userId, WorkspaceMode.Guided);

        _userPreferenceRepositoryMock
            .SetupSequence(repository => repository.GetByUserIdAsync(userId, default))
            .ReturnsAsync((UserPreference?)null)
            .ReturnsAsync(persistedPreference);

        var result = await _service.GetPreferencesAsync(userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be(userId);
        result.Value.WorkspaceMode.Should().Be(WorkspaceModeContract.Guided);
        _userPreferenceRepositoryMock.Verify(repository => repository.GetByUserIdAsync(userId, default), Times.Exactly(2));
    }

    [Fact]
    public async Task UpdatePreferencesAsync_ShouldReturnValidationError_WhenModeIsInvalid()
    {
        var result = await _service.UpdatePreferencesAsync(Guid.NewGuid(), new UpdateWorkspacePreferenceDto("unsupported"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("guided, workbench, agent");
    }

    [Fact]
    public async Task UpdatePreferencesAsync_ShouldUpdateModeWithoutLoadingFullWorkspaceAggregate()
    {
        var userId = Guid.NewGuid();
        var preference = new UserPreference(userId, WorkspaceMode.Guided);

        _userPreferenceRepositoryMock
            .Setup(repository => repository.GetByUserIdAsync(userId, default))
            .ReturnsAsync(preference);

        var result = await _service.UpdatePreferencesAsync(
            userId,
            new UpdateWorkspacePreferenceDto(WorkspaceModeContract.Agent));

        result.IsSuccess.Should().BeTrue();
        result.Value.WorkspaceMode.Should().Be(WorkspaceModeContract.Agent);
        _proposalRepositoryMock.Verify(repository => repository.GetByUserIdAsync(userId, int.MaxValue, default), Times.Never);
        _boardRepositoryMock.Verify(repository => repository.GetReadableByUserIdAsync(userId, false, default), Times.Never);
        _cardRepositoryMock.Verify(repository => repository.GetAgendaByBoardIdsAsync(It.IsAny<IEnumerable<Guid>>(), default), Times.Never);
        _llmQueueRepositoryMock.Verify(repository => repository.GetByUserAsync(userId, default), Times.Never);
    }

    [Fact]
    public async Task GetHomeAsync_ShouldReturnProductShapedSummary()
    {
        var userId = Guid.NewGuid();
        var boardA = new Board("Alpha", "Alpha board", userId);
        var boardB = new Board("Beta", "Beta board", userId);
        boardA.Update(description: "Alpha board updated");

        var preference = new UserPreference(userId, WorkspaceMode.Workbench);

        _userPreferenceRepositoryMock
            .Setup(repository => repository.GetByUserIdAsync(userId, default))
            .ReturnsAsync(preference);
        _boardRepositoryMock
            .Setup(repository => repository.CountReadableByUserIdAsync(userId, false, default))
            .ReturnsAsync(2);
        _boardRepositoryMock
            .Setup(repository => repository.CountReadableUpdatedSinceAsync(userId, It.IsAny<DateTimeOffset>(), false, default))
            .ReturnsAsync(2);
        _boardRepositoryMock
            .Setup(repository => repository.GetRecentReadableByUserIdAsync(userId, 3, false, default))
            .ReturnsAsync([boardA, boardB]);
        _llmQueueRepositoryMock
            .Setup(repository => repository.GetCaptureSummaryByUserAsync(userId, default))
            .ReturnsAsync((5, 1, 1, 1, 1));
        _proposalRepositoryMock
            .Setup(repository => repository.CountPendingReviewByUserIdAsync(userId, default))
            .ReturnsAsync(2);
        _proposalRepositoryMock
            .Setup(repository => repository.HasReviewedByUserIdAsync(userId, default))
            .ReturnsAsync(true);

        var result = await _service.GetHomeAsync(userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.WorkspaceMode.Should().Be(WorkspaceModeContract.Workbench);
        result.Value.IsFirstRun.Should().BeFalse();
        result.Value.Onboarding.IsComplete.Should().BeTrue();
        result.Value.Onboarding.CurrentStepId.Should().BeNull();
        result.Value.Workload.CapturesNeedingTriage.Should().Be(2);
        result.Value.Workload.CapturesInProgress.Should().Be(1);
        result.Value.Workload.CapturesReadyForFollowUp.Should().Be(1);
        result.Value.Workload.ProposalsPendingReview.Should().Be(2);
        result.Value.Boards.TotalBoards.Should().Be(2);
        result.Value.Boards.RecentBoardsCount.Should().Be(2);
        result.Value.Boards.RecentBoards.Should().HaveCount(2);
        result.Value.RecommendedActions.Select(action => action.ActionId).Should().Contain([
            "triage-captures",
            "review-proposals",
            "resume-recent-board",
            "capture-now"
        ]);
        _llmQueueRepositoryMock.Verify(repository => repository.GetByUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetTodayAsync_ShouldReturnAgendaCardsAndMarkOnboardingComplete()
    {
        var userId = Guid.NewGuid();
        var board = new Board("Alpha", "Alpha board", userId);
        var column = new Column(board.Id, "Backlog", 0);
        var overdueCard = new Card(board.Id, column.Id, "Overdue follow-up", dueDate: DateTimeOffset.UtcNow.AddDays(-1));
        var dueTodayCard = new Card(board.Id, column.Id, "Due today", dueDate: DateTimeOffset.UtcNow.AddHours(4));
        var blockedCard = new Card(board.Id, column.Id, "Blocked review");
        blockedCard.Block("Waiting on dependency");

        var preference = new UserPreference(userId, WorkspaceMode.Guided);

        _userPreferenceRepositoryMock
            .Setup(repository => repository.GetByUserIdAsync(userId, default))
            .ReturnsAsync(preference);
        _boardRepositoryMock
            .Setup(repository => repository.GetReadableByUserIdAsync(userId, false, default))
            .ReturnsAsync([board]);
        _llmQueueRepositoryMock
            .Setup(repository => repository.GetCaptureSummaryByUserAsync(userId, default))
            .ReturnsAsync((1, 1, 0, 0, 0));
        _proposalRepositoryMock
            .Setup(repository => repository.CountPendingReviewByUserIdAsync(userId, default))
            .ReturnsAsync(0);
        _proposalRepositoryMock
            .Setup(repository => repository.HasReviewedByUserIdAsync(userId, default))
            .ReturnsAsync(true);
        _cardRepositoryMock
            .Setup(repository => repository.GetAgendaByBoardIdsAsync(It.IsAny<IEnumerable<Guid>>(), default))
            .ReturnsAsync([overdueCard, dueTodayCard, blockedCard]);

        var result = await _service.GetTodayAsync(userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Onboarding.IsComplete.Should().BeTrue();
        result.Value.Onboarding.CurrentStepId.Should().BeNull();
        result.Value.Summary.OverdueCards.Should().Be(1);
        result.Value.Summary.DueTodayCards.Should().Be(1);
        result.Value.Summary.BlockedCards.Should().Be(1);
        result.Value.OverdueCards.Should().ContainSingle(card => card.Title == "Overdue follow-up");
        result.Value.DueTodayCards.Should().ContainSingle(card => card.Title == "Due today");
        result.Value.BlockedCards.Should().ContainSingle(card => card.BlockReason == "Waiting on dependency");
        preference.OnboardingCompletedAt.Should().NotBeNull();
        _proposalRepositoryMock.Verify(repository => repository.GetByUserIdAsync(userId, int.MaxValue, default), Times.Never);
        _cardRepositoryMock.Verify(repository => repository.GetByBoardIdsAsync(It.IsAny<IEnumerable<Guid>>(), default), Times.Never);
        _unitOfWorkMock.Verify(unitOfWork => unitOfWork.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task GetTodayAsync_ShouldBucketDueDatesUsingCardOffsetCalendarDay()
    {
        var userId = Guid.NewGuid();
        var board = new Board("Alpha", "Alpha board", userId);
        var column = new Column(board.Id, "Backlog", 0);
        var offset = TimeSpan.FromHours(14);
        var localToday = DateTimeOffset.UtcNow.ToOffset(offset).Date;
        var dueTodayWithPositiveOffset = new Card(
            board.Id,
            column.Id,
            "Offset due today",
            dueDate: new DateTimeOffset(localToday.Year, localToday.Month, localToday.Day, 0, 30, 0, offset));

        _userPreferenceRepositoryMock
            .Setup(repository => repository.GetByUserIdAsync(userId, default))
            .ReturnsAsync(new UserPreference(userId, WorkspaceMode.Guided));
        _boardRepositoryMock
            .Setup(repository => repository.GetReadableByUserIdAsync(userId, false, default))
            .ReturnsAsync([board]);
        _llmQueueRepositoryMock
            .Setup(repository => repository.GetCaptureSummaryByUserAsync(userId, default))
            .ReturnsAsync((0, 0, 0, 0, 0));
        _cardRepositoryMock
            .Setup(repository => repository.GetAgendaByBoardIdsAsync(It.IsAny<IEnumerable<Guid>>(), default))
            .ReturnsAsync([dueTodayWithPositiveOffset]);

        var result = await _service.GetTodayAsync(userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Summary.DueTodayCards.Should().Be(1);
        result.Value.DueTodayCards.Should().ContainSingle(card => card.Title == "Offset due today");
        result.Value.Summary.OverdueCards.Should().Be(0);
    }

    [Fact]
    public async Task UpdateOnboardingAsync_ShouldDismissAndReplayState()
    {
        var userId = Guid.NewGuid();
        var preference = new UserPreference(userId, WorkspaceMode.Guided);

        _userPreferenceRepositoryMock
            .Setup(repository => repository.GetByUserIdAsync(userId, default))
            .ReturnsAsync(preference);

        var dismissResult = await _service.UpdateOnboardingAsync(
            userId,
            new UpdateWorkspaceOnboardingDto(WorkspaceOnboardingActionContract.Dismiss));

        dismissResult.IsSuccess.Should().BeTrue();
        dismissResult.Value.Visibility.Should().Be(WorkspaceOnboardingVisibilityContract.Dismissed);
        preference.OnboardingDismissedAt.Should().NotBeNull();

        var replayResult = await _service.UpdateOnboardingAsync(
            userId,
            new UpdateWorkspaceOnboardingDto(WorkspaceOnboardingActionContract.Replay));

        replayResult.IsSuccess.Should().BeTrue();
        replayResult.Value.Visibility.Should().Be(WorkspaceOnboardingVisibilityContract.Active);
        preference.OnboardingDismissedAt.Should().BeNull();
    }
}
