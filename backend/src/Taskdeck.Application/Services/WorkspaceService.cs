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
    private const int TodayCardLimit = 5;
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
        var hasReviewedProposal = await _unitOfWork.AutomationProposals.HasReviewedByUserIdAsync(userId, cancellationToken);
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
        var onboarding = await BuildOnboardingAsync(
            preference,
            hasCapture: captureSummary.TotalCaptures > 0,
            hasReviewedProposal,
            hasBoard: totalBoards > 0,
            cancellationToken);
        var isFirstRun =
            totalBoards == 0 &&
            captureSummary.TotalCaptures == 0 &&
            proposalsPendingReview == 0;

        return Result.Success(new WorkspaceHomeDto(
            preference.WorkspaceMode.ToContractValue(),
            isFirstRun,
            onboarding,
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

    public async Task<Result<WorkspaceTodayDto>> GetTodayAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure<WorkspaceTodayDto>(ErrorCodes.ValidationError, "User ID cannot be empty");

        var preference = await EnsurePreferenceAsync(userId, cancellationToken);
        var captureSummary = await _unitOfWork.LlmQueue.GetCaptureSummaryByUserAsync(userId, cancellationToken);
        var capturesNeedingTriage = captureSummary.NewCount + captureSummary.FailedCount;
        var capturesReadyForFollowUp = captureSummary.TriagedCount;
        var proposalsPendingReview = await _unitOfWork.AutomationProposals.CountPendingReviewByUserIdAsync(userId, cancellationToken);
        var hasReviewedProposal = await _unitOfWork.AutomationProposals.HasReviewedByUserIdAsync(userId, cancellationToken);
        var accessibleBoards = (await _unitOfWork.Boards.GetReadableByUserIdAsync(
                userId,
                includeArchived: false,
                cancellationToken))
            .ToList();
        var agendaCards = accessibleBoards.Count == 0
            ? Array.Empty<Card>()
            : (await _unitOfWork.Cards.GetAgendaByBoardIdsAsync(
                    accessibleBoards.Select(board => board.Id),
                    cancellationToken))
                .ToArray();
        var onboarding = await BuildOnboardingAsync(
            preference,
            hasCapture: captureSummary.TotalCaptures > 0,
            hasReviewedProposal,
            hasBoard: accessibleBoards.Count > 0,
            cancellationToken);
        var boardsById = accessibleBoards.ToDictionary(board => board.Id);
        var referenceTime = DateTimeOffset.UtcNow;
        var overdueCards = BuildTodayCards(
            agendaCards,
            boardsById,
            card => ResolveDueBucket(card.DueDate, referenceTime) == TodayDueBucket.Overdue,
            cards => cards
                .OrderBy(card => card.DueDate)
                .ThenByDescending(card => card.UpdatedAt));
        var dueTodayCards = BuildTodayCards(
            agendaCards,
            boardsById,
            card => ResolveDueBucket(card.DueDate, referenceTime) == TodayDueBucket.DueToday,
            cards => cards
                .OrderBy(card => card.DueDate)
                .ThenByDescending(card => card.UpdatedAt));
        var blockedCards = BuildTodayCards(
            agendaCards,
            boardsById,
            card => card.IsBlocked,
            cards => cards
                .OrderBy(card => card.DueDate.HasValue ? 0 : 1)
                .ThenBy(card => card.DueDate)
                .ThenByDescending(card => card.UpdatedAt));
        var isFirstRun =
            accessibleBoards.Count == 0 &&
            captureSummary.TotalCaptures == 0 &&
            proposalsPendingReview == 0;
        var recentBoard = accessibleBoards.FirstOrDefault();

        return Result.Success(new WorkspaceTodayDto(
            preference.WorkspaceMode.ToContractValue(),
            onboarding,
            new WorkspaceTodaySummaryDto(
                capturesNeedingTriage,
                proposalsPendingReview,
                overdueCards.Count,
                dueTodayCards.Count,
                blockedCards.Count),
            overdueCards.Take(TodayCardLimit).ToList(),
            dueTodayCards.Take(TodayCardLimit).ToList(),
            blockedCards.Take(TodayCardLimit).ToList(),
            BuildRecommendedActions(
                isFirstRun,
                capturesNeedingTriage,
                capturesReadyForFollowUp,
                proposalsPendingReview,
                recentBoard is null
                    ? null
                    : new WorkspaceRecentBoardDto(
                        recentBoard.Id,
                        recentBoard.Name,
                        recentBoard.Description,
                        recentBoard.UpdatedAt))));
    }

    public async Task<Result<WorkspacePreferenceDto>> GetPreferencesAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure<WorkspacePreferenceDto>(ErrorCodes.ValidationError, "User ID cannot be empty");

        var preference = await EnsurePreferenceAsync(userId, cancellationToken);
        var onboarding = await GetOnboardingForPreferenceAsync(userId, preference, cancellationToken);
        return Result.Success(MapPreference(preference, onboarding));
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

        var onboarding = await GetOnboardingForPreferenceAsync(userId, preference, cancellationToken);
        return Result.Success(MapPreference(preference, onboarding));
    }

    public async Task<Result<WorkspaceOnboardingDto>> UpdateOnboardingAsync(
        Guid userId,
        UpdateWorkspaceOnboardingDto dto,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure<WorkspaceOnboardingDto>(ErrorCodes.ValidationError, "User ID cannot be empty");

        if (!WorkspaceOnboardingActionContract.TryParse(dto.Action, out var action))
        {
            return Result.Failure<WorkspaceOnboardingDto>(
                ErrorCodes.ValidationError,
                "Onboarding action must be one of: dismiss, replay");
        }

        var preference = await EnsurePreferenceAsync(userId, cancellationToken);

        switch (action)
        {
            case WorkspaceOnboardingAction.Dismiss:
                preference.DismissOnboarding();
                break;
            case WorkspaceOnboardingAction.Replay:
                preference.ReplayOnboarding();
                break;
            default:
                return Result.Failure<WorkspaceOnboardingDto>(
                    ErrorCodes.ValidationError,
                    $"Unsupported onboarding action '{action}'.");
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        var onboarding = await GetOnboardingForPreferenceAsync(userId, preference, cancellationToken);
        return Result.Success(onboarding);
    }

    private async Task<UserPreference> EnsurePreferenceAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _unitOfWork.UserPreferences.GetOrCreateDefaultByUserIdAsync(userId, cancellationToken);
    }

    private async Task<WorkspaceOnboardingDto> GetOnboardingForPreferenceAsync(
        Guid userId,
        UserPreference preference,
        CancellationToken cancellationToken)
    {
        if (preference.OnboardingCompletedAt is not null)
        {
            return MapOnboarding(
                preference,
                BuildOnboardingSteps(
                    hasCapture: true,
                    hasReviewedProposal: true,
                    hasBoard: true));
        }

        if (preference.OnboardingVisibility == WorkspaceOnboardingVisibility.Dismissed)
        {
            return MapDeferredOnboarding(preference);
        }

        var captureSummaryTask = _unitOfWork.LlmQueue.GetCaptureSummaryByUserAsync(userId, cancellationToken);
        var hasReviewedProposalTask = _unitOfWork.AutomationProposals.HasReviewedByUserIdAsync(userId, cancellationToken);
        var boardCountTask = _unitOfWork.Boards.CountReadableByUserIdAsync(
            userId,
            includeArchived: false,
            cancellationToken);

        await Task.WhenAll(captureSummaryTask, hasReviewedProposalTask, boardCountTask);

        return await BuildOnboardingAsync(
            preference,
            hasCapture: captureSummaryTask.Result.TotalCaptures > 0,
            hasReviewedProposal: hasReviewedProposalTask.Result,
            hasBoard: boardCountTask.Result > 0,
            cancellationToken);
    }

    private async Task<WorkspaceOnboardingDto> BuildOnboardingAsync(
        UserPreference preference,
        bool hasCapture,
        bool hasReviewedProposal,
        bool hasBoard,
        CancellationToken cancellationToken)
    {
        var steps = BuildOnboardingSteps(hasCapture, hasReviewedProposal, hasBoard);
        if (steps.All(step => step.IsComplete) && preference.OnboardingCompletedAt is null)
        {
            preference.RecordOnboardingCompletion();
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return MapOnboarding(preference, steps);
    }

    private static IReadOnlyList<WorkspaceOnboardingStepDto> BuildOnboardingSteps(
        bool hasCapture,
        bool hasReviewedProposal,
        bool hasBoard)
    {
        return
        [
            new WorkspaceOnboardingStepDto(
                "create-first-board",
                "Create your first board",
                "Start with a real destination so captures and proposals can land somewhere useful.",
                "boards",
                hasBoard),
            new WorkspaceOnboardingStepDto(
                "capture-first-item",
                "Capture one real task",
                "Drop a note, task, or follow-up into Inbox so the review loop has something to shape.",
                "capture",
                hasCapture),
            new WorkspaceOnboardingStepDto(
                "review-first-proposal",
                "Review your first proposal",
                "Use Review to decide what should reach a board before anything is applied.",
                "review",
                hasReviewedProposal)
        ];
    }

    private static WorkspaceOnboardingDto MapOnboarding(
        UserPreference preference,
        IReadOnlyList<WorkspaceOnboardingStepDto> steps)
    {
        var currentStepId = steps.FirstOrDefault(step => !step.IsComplete)?.StepId;

        return new WorkspaceOnboardingDto(
            preference.OnboardingVisibility.ToContractValue(),
            steps.All(step => step.IsComplete),
            currentStepId,
            preference.OnboardingDismissedAt,
            preference.OnboardingCompletedAt,
            steps);
    }

    private static WorkspaceOnboardingDto MapDeferredOnboarding(UserPreference preference)
    {
        return new WorkspaceOnboardingDto(
            preference.OnboardingVisibility.ToContractValue(),
            false,
            null,
            preference.OnboardingDismissedAt,
            preference.OnboardingCompletedAt,
            []);
    }

    private static WorkspacePreferenceDto MapPreference(
        UserPreference preference,
        WorkspaceOnboardingDto onboarding)
    {
        return new WorkspacePreferenceDto(
            preference.UserId,
            preference.WorkspaceMode.ToContractValue(),
            onboarding,
            preference.CreatedAt,
            preference.UpdatedAt);
    }

    private static IReadOnlyList<WorkspaceTodayCardDto> BuildTodayCards(
        IReadOnlyList<Card> cards,
        IReadOnlyDictionary<Guid, Board> boardsById,
        Func<Card, bool> filter,
        Func<IEnumerable<Card>, IOrderedEnumerable<Card>> order)
    {
        return order(cards.Where(filter))
            .Where(card => boardsById.ContainsKey(card.BoardId))
            .Select(card =>
            {
                var board = boardsById[card.BoardId];
                return new WorkspaceTodayCardDto(
                    card.BoardId,
                    board.Name,
                    card.Id,
                    card.Title,
                    card.DueDate,
                    card.BlockReason,
                    card.UpdatedAt);
            })
            .ToList();
    }

    private static TodayDueBucket? ResolveDueBucket(DateTimeOffset? dueDate, DateTimeOffset referenceTime)
    {
        if (!dueDate.HasValue)
        {
            return null;
        }

        var localToday = referenceTime.ToOffset(dueDate.Value.Offset).Date;
        var dueDateOnly = dueDate.Value.Date;

        if (dueDateOnly < localToday)
        {
            return TodayDueBucket.Overdue;
        }

        if (dueDateOnly == localToday)
        {
            return TodayDueBucket.DueToday;
        }

        return null;
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

    private enum TodayDueBucket
    {
        Overdue,
        DueToday
    }
}
