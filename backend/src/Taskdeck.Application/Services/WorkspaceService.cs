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

    public WorkspaceService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<WorkspaceHomeDto>> GetHomeAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure<WorkspaceHomeDto>(ErrorCodes.ValidationError, "User ID cannot be empty");

        var preference = await EnsurePreferenceAsync(userId, cancellationToken);
        var captureSummary = await _unitOfWork.LlmQueue.GetCaptureSummaryByUserAsync(userId, cancellationToken);
        var recentCutoff = DateTimeOffset.UtcNow.Subtract(RecentBoardWindow);
        var capturesNeedingTriage = captureSummary.NewCount + captureSummary.FailedCount;
        var capturesInProgress = captureSummary.TriagingCount;
        var capturesReadyForFollowUp = captureSummary.TriagedCount;
        var proposalsPendingReview = await _unitOfWork.AutomationProposals.CountPendingReviewByUserIdAsync(userId, cancellationToken);
        var totalBoards = await _unitOfWork.Boards.CountReadableByUserIdAsync(userId, includeArchived: false, cancellationToken);
        var recentBoardsCount = await _unitOfWork.Boards.CountReadableUpdatedSinceAsync(
            userId,
            recentCutoff,
            includeArchived: false,
            cancellationToken);
        var recentBoardCandidates = (await _unitOfWork.Boards.GetRecentReadableByUserIdAsync(
                userId,
                RecentBoardLimit,
                includeArchived: false,
                cancellationToken))
            .ToList();
        var recentBoards = recentBoardCandidates
            .Where(board => board.UpdatedAt >= recentCutoff)
            .Select(board => new WorkspaceRecentBoardDto(
                board.Id,
                board.Name,
                board.Description,
                board.UpdatedAt))
            .ToList();

        var isFirstRun =
            totalBoards == 0 &&
            captureSummary.TotalCaptures == 0 &&
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
                totalBoards,
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
        return await _unitOfWork.UserPreferences.GetOrCreateDefaultByUserIdAsync(userId, cancellationToken);
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
