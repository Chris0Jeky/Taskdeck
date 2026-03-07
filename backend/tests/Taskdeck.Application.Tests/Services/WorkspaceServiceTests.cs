using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
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
    private readonly Mock<IAuthorizationService> _authorizationServiceMock = new();
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
        _llmQueueRepositoryMock
            .Setup(repository => repository.GetByUserAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync([]);
        _boardRepositoryMock
            .Setup(repository => repository.SearchIdsAsync(null, false, default))
            .ReturnsAsync([]);
        _authorizationServiceMock
            .Setup(service => service.GetReadableBoardIdsAsync(It.IsAny<Guid>(), It.IsAny<IEnumerable<Guid>>(), default))
            .ReturnsAsync(Result.Success<IReadOnlySet<Guid>>(new HashSet<Guid>()));
        _cardRepositoryMock
            .Setup(repository => repository.GetByBoardIdsAsync(It.IsAny<IEnumerable<Guid>>(), default))
            .ReturnsAsync([]);

        _service = new WorkspaceService(_unitOfWorkMock.Object, _authorizationServiceMock.Object);
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

        var pendingProposal = new AutomationProposal(
            ProposalSourceType.Queue,
            userId,
            "Pending proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString("N"),
            boardA.Id);
        var approvedProposal = new AutomationProposal(
            ProposalSourceType.Queue,
            userId,
            "Approved proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString("N"),
            boardA.Id);
        approvedProposal.Approve(userId);
        var anotherPendingProposal = new AutomationProposal(
            ProposalSourceType.Chat,
            userId,
            "Another pending proposal",
            RiskLevel.Medium,
            Guid.NewGuid().ToString("N"),
            boardB.Id);

        _userPreferenceRepositoryMock
            .Setup(repository => repository.GetByUserIdAsync(userId, default))
            .ReturnsAsync(preference);
        _boardRepositoryMock
            .Setup(repository => repository.SearchIdsAsync(null, false, default))
            .ReturnsAsync([boardA.Id, boardB.Id, Guid.NewGuid()]);
        _authorizationServiceMock
            .Setup(service => service.GetReadableBoardIdsAsync(userId, It.IsAny<IEnumerable<Guid>>(), default))
            .ReturnsAsync(Result.Success<IReadOnlySet<Guid>>(new HashSet<Guid> { boardA.Id, boardB.Id }));
        _boardRepositoryMock
            .Setup(repository => repository.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), default))
            .ReturnsAsync([boardA, boardB]);
        _llmQueueRepositoryMock
            .Setup(repository => repository.GetByUserAsync(userId, default))
            .ReturnsAsync([pendingCapture, triagingCapture, triagedCapture, failedCapture, proposalCreatedCapture]);
        _proposalRepositoryMock
            .Setup(repository => repository.GetByUserIdAsync(userId, int.MaxValue, default))
            .ReturnsAsync([pendingProposal, approvedProposal, anotherPendingProposal]);

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
        var capture = CreateCaptureRequest(userId, RequestStatus.Pending);
        var reviewedProposal = new AutomationProposal(
            ProposalSourceType.Queue,
            userId,
            "Reviewed proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString("N"),
            board.Id);
        reviewedProposal.Approve(userId);

        _userPreferenceRepositoryMock
            .Setup(repository => repository.GetByUserIdAsync(userId, default))
            .ReturnsAsync(preference);
        _boardRepositoryMock
            .Setup(repository => repository.SearchIdsAsync(null, false, default))
            .ReturnsAsync([board.Id]);
        _authorizationServiceMock
            .Setup(service => service.GetReadableBoardIdsAsync(userId, It.IsAny<IEnumerable<Guid>>(), default))
            .ReturnsAsync(Result.Success<IReadOnlySet<Guid>>(new HashSet<Guid> { board.Id }));
        _boardRepositoryMock
            .Setup(repository => repository.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), default))
            .ReturnsAsync([board]);
        _llmQueueRepositoryMock
            .Setup(repository => repository.GetByUserAsync(userId, default))
            .ReturnsAsync([capture]);
        _proposalRepositoryMock
            .Setup(repository => repository.GetByUserIdAsync(userId, int.MaxValue, default))
            .ReturnsAsync([reviewedProposal]);
        _cardRepositoryMock
            .Setup(repository => repository.GetByBoardIdsAsync(It.IsAny<IEnumerable<Guid>>(), default))
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
        _unitOfWorkMock.Verify(unitOfWork => unitOfWork.SaveChangesAsync(default), Times.Once);
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
