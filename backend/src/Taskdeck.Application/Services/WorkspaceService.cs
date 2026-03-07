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
        var captureStatuses = await GetCaptureStatusesAsync(userId, cancellationToken);
        var accessibleBoards = (await _unitOfWork.Boards.GetReadableByUserIdAsync(
                userId,
                includeArchived: false,
                cancellationToken))
            .ToList();
        var recentCutoff = DateTimeOffset.UtcNow.Subtract(RecentBoardWindow);
        var capturesNeedingTriage = captureStatuses.Count(status => status is CaptureStatus.New or CaptureStatus.Failed);
        var capturesInProgress = captureStatuses.Count(status => status == CaptureStatus.Triaging);
        var capturesReadyForFollowUp = captureStatuses.Count(status => status == CaptureStatus.Triaged);
        var proposalsPendingReview = await _unitOfWork.AutomationProposals.CountPendingReviewByUserIdAsync(userId, cancellationToken);
        var hasReviewedProposal = await _unitOfWork.AutomationProposals.HasReviewedByUserIdAsync(userId, cancellationToken);
        var onboarding = await BuildOnboardingAsync(
            preference,
            hasCapture: captureStatuses.Count > 0,
            hasReviewedProposal,
            hasBoard: accessibleBoards.Count > 0,
            cancellationToken);
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
            onboarding,
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

    public async Task<Result<WorkspaceTodayDto>> GetTodayAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure<WorkspaceTodayDto>(ErrorCodes.ValidationError, "User ID cannot be empty");

        var aggregateResult = await GetWorkspaceAggregateAsync(userId, includeCards: true, cancellationToken);
        if (!aggregateResult.IsSuccess)
            return Result.Failure<WorkspaceTodayDto>(aggregateResult.ErrorCode, aggregateResult.ErrorMessage);

        var aggregate = aggregateResult.Value;
        var capturesNeedingTriage = aggregate.CaptureStatuses.Count(status => status is CaptureStatus.New or CaptureStatus.Failed);
        var capturesReadyForFollowUp = aggregate.CaptureStatuses.Count(status => status == CaptureStatus.Triaged);
        var proposalsPendingReview = aggregate.Proposals.Count(proposal => proposal.Status == ProposalStatus.PendingReview);
        var today = DateTime.UtcNow.Date;
        var boardsById = aggregate.AccessibleBoards.ToDictionary(board => board.Id);
        var overdueCards = BuildTodayCards(
            aggregate.Cards,
            boardsById,
            card => card.DueDate.HasValue && card.DueDate.Value.UtcDateTime.Date < today,
            cards => cards
                .OrderBy(card => card.DueDate)
                .ThenByDescending(card => card.UpdatedAt));
        var dueTodayCards = BuildTodayCards(
            aggregate.Cards,
            boardsById,
            card => card.DueDate.HasValue && card.DueDate.Value.UtcDateTime.Date == today,
            cards => cards
                .OrderBy(card => card.DueDate)
                .ThenByDescending(card => card.UpdatedAt));
        var blockedCards = BuildTodayCards(
            aggregate.Cards,
            boardsById,
            card => card.IsBlocked,
            cards => cards
                .OrderBy(card => card.DueDate.HasValue ? 0 : 1)
                .ThenBy(card => card.DueDate)
                .ThenByDescending(card => card.UpdatedAt));
        var isFirstRun =
            aggregate.AccessibleBoards.Count == 0 &&
            aggregate.CaptureStatuses.Count == 0 &&
            aggregate.Proposals.Count == 0;
        var recentBoard = aggregate.AccessibleBoards.FirstOrDefault();

        return Result.Success(new WorkspaceTodayDto(
            aggregate.Preference.WorkspaceMode.ToContractValue(),
            aggregate.Onboarding,
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

    private async Task<Result<WorkspaceAggregate>> GetWorkspaceAggregateAsync(
        Guid userId,
        bool includeCards,
        CancellationToken cancellationToken)
    {
        var preference = await EnsurePreferenceAsync(userId, cancellationToken);
        var captureStatuses = await GetCaptureStatusesAsync(userId, cancellationToken);
        var proposals = (await _unitOfWork.AutomationProposals.GetByUserIdAsync(userId, int.MaxValue, cancellationToken))
            .ToList();
        var accessibleBoards = (await _unitOfWork.Boards.GetReadableByUserIdAsync(
                userId,
                includeArchived: false,
                cancellationToken))
            .ToList();

        IReadOnlyList<Card> cards = [];
        if (includeCards && accessibleBoards.Count > 0)
        {
            cards = (await _unitOfWork.Cards.GetByBoardIdsAsync(
                    accessibleBoards.Select(board => board.Id),
                    cancellationToken))
                .ToList();
        }

        var onboarding = await BuildOnboardingAsync(
            preference,
            hasCapture: captureStatuses.Count > 0,
            hasReviewedProposal: proposals.Any(HasReviewedOutcome),
            hasBoard: accessibleBoards.Count > 0,
            cancellationToken);

        return Result.Success(new WorkspaceAggregate(
            preference,
            captureStatuses,
            proposals,
            accessibleBoards,
            cards,
            onboarding));
    }

    private async Task<UserPreference> EnsurePreferenceAsync(Guid userId, CancellationToken cancellationToken)
    {
        var preference = await _unitOfWork.UserPreferences.GetByUserIdAsync(userId, cancellationToken);
        if (preference is not null)
            return preference;

        var defaultPreference = UserPreference.CreateDefault(userId);
        await _unitOfWork.UserPreferences.AddAsync(defaultPreference, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return await _unitOfWork.UserPreferences.GetByUserIdAsync(userId, cancellationToken) ?? defaultPreference;
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

    private async Task<WorkspaceOnboardingDto> GetOnboardingForPreferenceAsync(
        Guid userId,
        UserPreference preference,
        CancellationToken cancellationToken)
    {
        var captureStatuses = await GetCaptureStatusesAsync(userId, cancellationToken);
        var hasReviewedProposal = await _unitOfWork.AutomationProposals.HasReviewedByUserIdAsync(userId, cancellationToken);
        var hasBoard = await _unitOfWork.Boards.CountReadableByUserIdAsync(userId, includeArchived: false, cancellationToken) > 0;

        return await BuildOnboardingAsync(
            preference,
            hasCapture: captureStatuses.Count > 0,
            hasReviewedProposal,
            hasBoard,
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

    private static CaptureStatus ResolveCaptureStatus(LlmRequest item)
    {
        var payloadResult = CaptureRequestContract.ParsePayload(item.Payload, allowServerAttributionFields: true);
        var hasLinkedProposal = payloadResult.IsSuccess &&
                                payloadResult.Value.Provenance?.ProposalId is Guid proposalId &&
                                proposalId != Guid.Empty;

        return CaptureStatusPolicy.MapFromQueueStatus(item.Status, hasLinkedProposal);
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

    private static bool HasReviewedOutcome(AutomationProposal proposal)
    {
        return proposal.Status is ProposalStatus.Approved
            or ProposalStatus.Rejected
            or ProposalStatus.Applied
            or ProposalStatus.Failed
            or ProposalStatus.Expired;
    }

    private sealed record WorkspaceAggregate(
        UserPreference Preference,
        IReadOnlyList<CaptureStatus> CaptureStatuses,
        IReadOnlyList<AutomationProposal> Proposals,
        IReadOnlyList<Board> AccessibleBoards,
        IReadOnlyList<Card> Cards,
        WorkspaceOnboardingDto Onboarding);
}
