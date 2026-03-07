using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class WorkspaceService : IWorkspaceService
{
    private const int RecentBoardLimit = 3;
    private static readonly TimeSpan RecentBoardWindow = TimeSpan.FromDays(14);

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuthorizationService _authorizationService;

    public WorkspaceService(
        IUnitOfWork unitOfWork,
        IAuthorizationService authorizationService)
    {
        _unitOfWork = unitOfWork;
        _authorizationService = authorizationService;
    }

    public async Task<Result<WorkspaceHomeDto>> GetHomeAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure<WorkspaceHomeDto>(ErrorCodes.ValidationError, "User ID cannot be empty");

        var preference = await EnsurePreferenceAsync(userId, cancellationToken);
        var captureStatuses = await GetCaptureStatusesAsync(userId, cancellationToken);
        var proposals = await _unitOfWork.AutomationProposals.GetByUserIdAsync(userId, int.MaxValue, cancellationToken);
        var accessibleBoardsResult = await GetAccessibleBoardsAsync(userId, cancellationToken);
        if (!accessibleBoardsResult.IsSuccess)
            return Result.Failure<WorkspaceHomeDto>(accessibleBoardsResult.ErrorCode, accessibleBoardsResult.ErrorMessage);

        var accessibleBoards = accessibleBoardsResult.Value
            .OrderByDescending(board => board.UpdatedAt)
            .ThenByDescending(board => board.CreatedAt)
            .ToList();
        var recentCutoff = DateTimeOffset.UtcNow.Subtract(RecentBoardWindow);
        var capturesNeedingTriage = captureStatuses.Count(status => status is CaptureStatus.New or CaptureStatus.Failed);
        var capturesInProgress = captureStatuses.Count(status => status == CaptureStatus.Triaging);
        var capturesReadyForFollowUp = captureStatuses.Count(status => status == CaptureStatus.Triaged);
        var proposalsPendingReview = proposals.Count(proposal => proposal.Status == ProposalStatus.PendingReview);
        var recentBoards = accessibleBoards
            .Take(RecentBoardLimit)
            .Select(board => new WorkspaceRecentBoardDto(
                board.Id,
                board.Name,
                board.Description,
                board.UpdatedAt))
            .ToList();
        var recentBoardsCount = accessibleBoards.Count(board => board.UpdatedAt >= recentCutoff);

        var isFirstRun =
            accessibleBoards.Count == 0 &&
            captureStatuses.Count == 0 &&
            proposalsPendingReview == 0;

        return Result.Success(new WorkspaceHomeDto(
            preference.WorkspaceMode.ToContractValue(),
            isFirstRun,
            new WorkspaceHomeWorkloadDto(
                capturesNeedingTriage,
                capturesInProgress,
                capturesReadyForFollowUp,
                proposalsPendingReview),
            new WorkspaceBoardSummaryDto(
                accessibleBoards.Count,
                recentBoardsCount,
                recentBoards),
            BuildRecommendedActions(
                isFirstRun,
                capturesNeedingTriage,
                capturesReadyForFollowUp,
                proposalsPendingReview,
                recentBoards.FirstOrDefault())));
    }

    public async Task<Result<WorkspacePreferenceDto>> GetPreferencesAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure<WorkspacePreferenceDto>(ErrorCodes.ValidationError, "User ID cannot be empty");

        var preference = await EnsurePreferenceAsync(userId, cancellationToken);
        return Result.Success(MapPreference(preference));
    }

    public async Task<Result<WorkspacePreferenceDto>> UpdatePreferencesAsync(
        Guid userId,
        UpdateWorkspacePreferenceDto dto,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure<WorkspacePreferenceDto>(ErrorCodes.ValidationError, "User ID cannot be empty");

        if (!WorkspaceModeContract.TryParse(dto.WorkspaceMode, out var workspaceMode))
        {
            return Result.Failure<WorkspacePreferenceDto>(
                ErrorCodes.ValidationError,
                "Workspace mode must be one of: guided, workbench, agent");
        }

        var preference = await EnsurePreferenceAsync(userId, cancellationToken);

        try
        {
            preference.UpdateWorkspaceMode(workspaceMode);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DomainException ex)
        {
            return Result.Failure<WorkspacePreferenceDto>(ex.ErrorCode, ex.Message);
        }

        return Result.Success(MapPreference(preference));
    }

    private async Task<UserPreference> EnsurePreferenceAsync(Guid userId, CancellationToken cancellationToken)
    {
        var preference = await _unitOfWork.UserPreferences.GetByUserIdAsync(userId, cancellationToken);
        if (preference is not null)
            return preference;

        preference = UserPreference.CreateDefault(userId);
        await _unitOfWork.UserPreferences.AddAsync(preference, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return preference;
    }

    private async Task<Result<IReadOnlyList<Board>>> GetAccessibleBoardsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var candidateBoardIds = (await _unitOfWork.Boards.SearchIdsAsync(
                searchText: null,
                includeArchived: false,
                cancellationToken))
            .ToList();

        if (candidateBoardIds.Count == 0)
            return Result.Success<IReadOnlyList<Board>>(Array.Empty<Board>());

        var readableBoardIdsResult = await _authorizationService.GetReadableBoardIdsAsync(
            userId,
            candidateBoardIds,
            cancellationToken);
        if (!readableBoardIdsResult.IsSuccess)
        {
            return Result.Failure<IReadOnlyList<Board>>(
                readableBoardIdsResult.ErrorCode,
                readableBoardIdsResult.ErrorMessage);
        }

        var readableBoardIds = readableBoardIdsResult.Value.ToList();
        if (readableBoardIds.Count == 0)
            return Result.Success<IReadOnlyList<Board>>(Array.Empty<Board>());

        var boards = await _unitOfWork.Boards.GetByIdsAsync(readableBoardIds, cancellationToken);
        return Result.Success<IReadOnlyList<Board>>(boards.ToList());
    }

    private async Task<IReadOnlyList<CaptureStatus>> GetCaptureStatusesAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var items = await _unitOfWork.LlmQueue.GetByUserAsync(userId, cancellationToken);

        return items
            .Where(item => CaptureRequestContract.IsCaptureRequestType(item.RequestType))
            .Select(ResolveCaptureStatus)
            .ToList();
    }

    private static CaptureStatus ResolveCaptureStatus(LlmRequest item)
    {
        var payloadResult = CaptureRequestContract.ParsePayload(item.Payload, allowServerAttributionFields: true);
        var hasLinkedProposal = payloadResult.IsSuccess &&
                                payloadResult.Value.Provenance?.ProposalId is Guid proposalId &&
                                proposalId != Guid.Empty;

        return CaptureStatusPolicy.MapFromQueueStatus(item.Status, hasLinkedProposal);
    }

    private static WorkspacePreferenceDto MapPreference(UserPreference preference)
    {
        return new WorkspacePreferenceDto(
            preference.UserId,
            preference.WorkspaceMode.ToContractValue(),
            preference.CreatedAt,
            preference.UpdatedAt);
    }

    private static IReadOnlyList<WorkspaceNextActionDto> BuildRecommendedActions(
        bool isFirstRun,
        int capturesNeedingTriage,
        int capturesReadyForFollowUp,
        int proposalsPendingReview,
        WorkspaceRecentBoardDto? mostRecentBoard)
    {
        var actions = new List<WorkspaceNextActionDto>();

        if (isFirstRun)
        {
            actions.Add(new WorkspaceNextActionDto(
                "create-first-board",
                "Create your first board",
                "Start with a board so captures and proposals have a clear destination.",
                "boards"));
        }

        if (capturesNeedingTriage > 0)
        {
            actions.Add(new WorkspaceNextActionDto(
                "triage-captures",
                "Triage new captures",
                $"{capturesNeedingTriage} capture(s) still need triage before they can move into review.",
                "capture",
                AttentionCount: capturesNeedingTriage));
        }

        if (proposalsPendingReview > 0)
        {
            actions.Add(new WorkspaceNextActionDto(
                "review-proposals",
                "Review pending proposals",
                $"{proposalsPendingReview} proposal(s) are waiting for review.",
                "review",
                AttentionCount: proposalsPendingReview));
        }

        if (capturesReadyForFollowUp > 0)
        {
            actions.Add(new WorkspaceNextActionDto(
                "follow-up-triaged-captures",
                "Follow up on triaged captures",
                $"{capturesReadyForFollowUp} capture(s) finished triage without a linked proposal yet.",
                "capture",
                AttentionCount: capturesReadyForFollowUp));
        }

        if (mostRecentBoard is not null)
        {
            actions.Add(new WorkspaceNextActionDto(
                "resume-recent-board",
                $"Resume {mostRecentBoard.Name}",
                "Jump back into your most recently active board.",
                "board",
                BoardId: mostRecentBoard.Id));
        }

        actions.Add(new WorkspaceNextActionDto(
            "capture-now",
            "Capture something new",
            "Drop a note, task, or idea into the inbox to keep the review loop moving.",
            "capture"));

        return actions
            .GroupBy(action => action.ActionId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
    }
}
