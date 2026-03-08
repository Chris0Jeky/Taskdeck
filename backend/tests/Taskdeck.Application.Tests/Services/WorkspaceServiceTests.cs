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
    private readonly Mock<ILlmQueueRepository> _llmQueueRepositoryMock = new();
    private readonly Mock<IAutomationProposalRepository> _proposalRepositoryMock = new();
    private readonly WorkspaceService _service;

    public WorkspaceServiceTests()
    {
        _unitOfWorkMock.SetupGet(unitOfWork => unitOfWork.UserPreferences).Returns(_userPreferenceRepositoryMock.Object);
        _unitOfWorkMock.SetupGet(unitOfWork => unitOfWork.Boards).Returns(_boardRepositoryMock.Object);
        _unitOfWorkMock.SetupGet(unitOfWork => unitOfWork.LlmQueue).Returns(_llmQueueRepositoryMock.Object);
        _unitOfWorkMock.SetupGet(unitOfWork => unitOfWork.AutomationProposals).Returns(_proposalRepositoryMock.Object);
        _unitOfWorkMock.Setup(unitOfWork => unitOfWork.SaveChangesAsync(default)).ReturnsAsync(1);

        _service = new WorkspaceService(_unitOfWorkMock.Object);
    }

    [Fact]
    public async Task GetPreferencesAsync_ShouldCreateDefaultGuidedPreference_WhenNoneExists()
    {
        var userId = Guid.NewGuid();
        var defaultPreference = UserPreference.CreateDefault(userId);

        _userPreferenceRepositoryMock
            .Setup(repository => repository.GetOrCreateDefaultByUserIdAsync(userId, default))
            .ReturnsAsync(defaultPreference);

        var result = await _service.GetPreferencesAsync(userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.WorkspaceMode.Should().Be(WorkspaceModeContract.Guided);
        _userPreferenceRepositoryMock.Verify(
            repository => repository.GetOrCreateDefaultByUserIdAsync(userId, default),
            Times.Once);
    }

    [Fact]
    public async Task GetPreferencesAsync_ShouldReturnPersistedPreference_WhenConcurrentCreateWins()
    {
        var userId = Guid.NewGuid();
        var persistedPreference = new UserPreference(userId, WorkspaceMode.Guided);

        _userPreferenceRepositoryMock
            .Setup(repository => repository.GetOrCreateDefaultByUserIdAsync(userId, default))
            .ReturnsAsync(persistedPreference);

        var result = await _service.GetPreferencesAsync(userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be(userId);
        result.Value.WorkspaceMode.Should().Be(WorkspaceModeContract.Guided);
        _userPreferenceRepositoryMock.Verify(
            repository => repository.GetOrCreateDefaultByUserIdAsync(userId, default),
            Times.Once);
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
    public async Task GetHomeAsync_ShouldReturnProductShapedSummary()
    {
        var userId = Guid.NewGuid();
        var boardA = new Board("Alpha", "Alpha board", userId);
        var boardB = new Board("Beta", "Beta board", userId);
        boardA.Update(description: "Alpha board updated");

        var preference = new UserPreference(userId, WorkspaceMode.Workbench);

        _userPreferenceRepositoryMock
            .Setup(repository => repository.GetOrCreateDefaultByUserIdAsync(userId, default))
            .ReturnsAsync(preference);
        _boardRepositoryMock
            .Setup(repository => repository.CountReadableByUserIdAsync(userId, false, default))
            .ReturnsAsync(2);
        _boardRepositoryMock
            .Setup(repository => repository.CountReadableUpdatedSinceAsync(
                userId,
                It.IsAny<DateTimeOffset>(),
                false,
                default))
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

        var result = await _service.GetHomeAsync(userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.WorkspaceMode.Should().Be(WorkspaceModeContract.Workbench);
        result.Value.IsFirstRun.Should().BeFalse();
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
    public async Task GetHomeAsync_ShouldOnlyReturnBoardsThatMatchTheRecentCutoff()
    {
        var userId = Guid.NewGuid();
        var recentBoard = new Board("Recent", "Recent board", userId);
        var staleBoard = new Board("Stale", "Stale board", userId);
        staleBoard.Update(description: "Still stale");

        var staleUpdatedAt = DateTimeOffset.UtcNow.Subtract(TimeSpan.FromDays(30));
        typeof(Board).GetProperty(nameof(Board.UpdatedAt))!
            .SetValue(staleBoard, staleUpdatedAt);

        _userPreferenceRepositoryMock
            .Setup(repository => repository.GetOrCreateDefaultByUserIdAsync(userId, default))
            .ReturnsAsync(new UserPreference(userId, WorkspaceMode.Guided));
        _boardRepositoryMock
            .Setup(repository => repository.CountReadableByUserIdAsync(userId, false, default))
            .ReturnsAsync(2);
        _boardRepositoryMock
            .Setup(repository => repository.CountReadableUpdatedSinceAsync(userId, It.IsAny<DateTimeOffset>(), false, default))
            .ReturnsAsync(1);
        _boardRepositoryMock
            .Setup(repository => repository.GetRecentReadableByUserIdAsync(userId, 3, false, default))
            .ReturnsAsync([recentBoard, staleBoard]);

        var result = await _service.GetHomeAsync(userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Boards.RecentBoardsCount.Should().Be(1);
        result.Value.Boards.RecentBoards.Should().ContainSingle(board => board.Name == "Recent");
        result.Value.RecommendedActions.Should().ContainSingle(action =>
            action.ActionId == "resume-recent-board" &&
            action.BoardId == recentBoard.Id);
    }
}
