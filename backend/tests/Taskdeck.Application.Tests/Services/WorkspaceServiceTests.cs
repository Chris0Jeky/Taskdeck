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
        _userPreferenceRepositoryMock
            .Setup(repository => repository.AddAsync(It.IsAny<UserPreference>(), default))
            .ReturnsAsync((UserPreference preference, CancellationToken _) => preference);

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
        _userPreferenceRepositoryMock.Verify(repository => repository.AddAsync(It.IsAny<UserPreference>(), default), Times.Once);
        _unitOfWorkMock.Verify(unitOfWork => unitOfWork.SaveChangesAsync(default), Times.Once);
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
    public async Task GetHomeAsync_ShouldReturnProductShapedSummary()
    {
        var userId = Guid.NewGuid();
        var boardA = new Board("Alpha", "Alpha board", userId);
        var boardB = new Board("Beta", "Beta board", userId);
        boardA.Update(description: "Alpha board updated");

        var preference = new UserPreference(userId, WorkspaceMode.Workbench);
        var pendingCapture = CreateCaptureRequest(userId, RequestStatus.Pending);
        var triagingCapture = CreateCaptureRequest(userId, RequestStatus.Processing);
        var triagedCapture = CreateCaptureRequest(userId, RequestStatus.Completed);
        var failedCapture = CreateCaptureRequest(userId, RequestStatus.Failed);
        var proposalCreatedCapture = CreateCaptureRequest(userId, RequestStatus.Completed, Guid.NewGuid());

        _userPreferenceRepositoryMock
            .Setup(repository => repository.GetByUserIdAsync(userId, default))
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
            .Setup(repository => repository.GetByUserAsync(userId, default))
            .ReturnsAsync([pendingCapture, triagingCapture, triagedCapture, failedCapture, proposalCreatedCapture]);
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
    }

    private static LlmRequest CreateCaptureRequest(Guid userId, RequestStatus status, Guid? proposalId = null)
    {
        var payload = new CapturePayloadV1(
            CaptureRequestContract.CurrentSchemaVersion,
            CaptureSource.Typed,
            $"Capture for {status}");
        var request = new LlmRequest(
            userId,
            CaptureRequestContract.RequestTypeV1,
            CaptureRequestContract.SerializePayload(payload));

        if (proposalId.HasValue)
        {
            var payloadWithProvenance = CaptureRequestContract.WithProvenance(
                payload,
                request.Id,
                proposalId: proposalId,
                requestedByUserId: userId,
                correlationId: Guid.NewGuid().ToString("N"),
                sourceSurface: "capture");
            request.UpdatePayload(CaptureRequestContract.SerializePayload(payloadWithProvenance));
        }

        switch (status)
        {
            case RequestStatus.Pending:
                break;
            case RequestStatus.Processing:
                request.MarkAsProcessing();
                break;
            case RequestStatus.Completed:
                request.MarkAsProcessing();
                request.MarkAsCompleted();
                break;
            case RequestStatus.Failed:
                request.MarkAsFailed("triage failed");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported capture status for test setup.");
        }

        return request;
    }
}
