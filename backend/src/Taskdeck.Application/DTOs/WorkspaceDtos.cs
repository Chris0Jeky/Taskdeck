namespace Taskdeck.Application.DTOs;

public record WorkspacePreferenceDto(
    Guid UserId,
    string WorkspaceMode,
    WorkspaceOnboardingDto Onboarding,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record UpdateWorkspacePreferenceDto(
    string WorkspaceMode);

public record UpdateWorkspaceOnboardingDto(
    string Action);

public record WorkspaceHomeDto(
    string WorkspaceMode,
    bool IsFirstRun,
    WorkspaceOnboardingDto Onboarding,
    WorkspaceHomeWorkloadDto Workload,
    WorkspaceBoardSummaryDto Boards,
    IReadOnlyList<WorkspaceNextActionDto> RecommendedActions);

public record WorkspaceTodayDto(
    string WorkspaceMode,
    WorkspaceOnboardingDto Onboarding,
    WorkspaceTodaySummaryDto Summary,
    IReadOnlyList<WorkspaceTodayCardDto> OverdueCards,
    IReadOnlyList<WorkspaceTodayCardDto> DueTodayCards,
    IReadOnlyList<WorkspaceTodayCardDto> BlockedCards,
    IReadOnlyList<WorkspaceNextActionDto> RecommendedActions);

public record WorkspaceHomeWorkloadDto(
    int CapturesNeedingTriage,
    int CapturesInProgress,
    int CapturesReadyForFollowUp,
    int ProposalsPendingReview);

public record WorkspaceBoardSummaryDto(
    int TotalBoards,
    int RecentBoardsCount,
    IReadOnlyList<WorkspaceRecentBoardDto> RecentBoards);

public record WorkspaceRecentBoardDto(
    Guid Id,
    string Name,
    string? Description,
    DateTimeOffset UpdatedAt);

public record WorkspaceOnboardingDto(
    string Visibility,
    bool IsComplete,
    string? CurrentStepId,
    DateTimeOffset? DismissedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<WorkspaceOnboardingStepDto> Steps);

public record WorkspaceOnboardingStepDto(
    string StepId,
    string Title,
    string Description,
    string TargetSurface,
    bool IsComplete);

public record WorkspaceTodaySummaryDto(
    int CapturesNeedingTriage,
    int ProposalsPendingReview,
    int OverdueCards,
    int DueTodayCards,
    int BlockedCards);

public record WorkspaceTodayCardDto(
    Guid BoardId,
    string BoardName,
    Guid CardId,
    string Title,
    DateTimeOffset? DueDate,
    string? BlockReason,
    DateTimeOffset UpdatedAt);

public record WorkspaceNextActionDto(
    string ActionId,
    string Title,
    string Description,
    string TargetSurface,
    Guid? BoardId = null,
    int? AttentionCount = null);

public record WorkspaceCalendarCardDto(
    Guid CardId,
    Guid BoardId,
    string BoardName,
    Guid ColumnId,
    string ColumnName,
    string Title,
    DateTimeOffset DueDate,
    bool IsBlocked,
    string? BlockReason,
    bool IsOverdue,
    DateTimeOffset UpdatedAt);

public record WorkspaceCalendarDto(
    DateTimeOffset From,
    DateTimeOffset To,
    int TotalCards,
    IReadOnlyList<WorkspaceCalendarCardDto> Cards);
