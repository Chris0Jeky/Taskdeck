namespace Taskdeck.Application.DTOs;

public record WorkspacePreferenceDto(
    Guid UserId,
    string WorkspaceMode,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record UpdateWorkspacePreferenceDto(
    string WorkspaceMode);

public record WorkspaceHomeDto(
    string WorkspaceMode,
    bool IsFirstRun,
    WorkspaceHomeWorkloadDto Workload,
    WorkspaceBoardSummaryDto Boards,
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

public record WorkspaceNextActionDto(
    string ActionId,
    string Title,
    string Description,
    string TargetSurface,
    Guid? BoardId = null,
    int? AttentionCount = null);
