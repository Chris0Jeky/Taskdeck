using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.DTOs;

public record CardDto(
    Guid Id,
    Guid BoardId,
    Guid ColumnId,
    string Title,
    string Description,
    DateTimeOffset? DueDate,
    bool IsBlocked,
    string? BlockReason,
    int Position,
    List<LabelDto> Labels,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool IsArchived = false,
    string WorkItemType = "Task",
    Guid? ParentCardId = null,
    IReadOnlyList<CardAssignmentDto>? Assignments = null
);

public record CardAssignmentDto(Guid UserId, string DisplayName, DateTimeOffset AssignedAt, Guid AssignedByUserId);
public record BoardParticipantDto(Guid UserId, string DisplayName);
public record ReplaceCardAssignmentsDto(IReadOnlyList<Guid>? UserIds, DateTimeOffset? ExpectedUpdatedAt);

public record CardLifecycleDto(DateTimeOffset? ExpectedUpdatedAt, string? ExpectedChildrenFingerprint = null);

public record CardDetachChildDto(Guid Id, Guid? ParentCardId, string Title, bool IsArchived, DateTimeOffset UpdatedAt);
public record CardDetachPreviewDto(Guid CardId, DateTimeOffset ExpectedUpdatedAt, string ExpectedChildrenFingerprint, IReadOnlyList<CardDetachChildDto> Children);

public record CreateCardDto(
    Guid BoardId,
    Guid ColumnId,
    string Title,
    string? Description,
    DateTimeOffset? DueDate,
    List<Guid>? LabelIds,
    string? WorkItemType = null,
    Guid? ParentCardId = null
);

public record UpdateCardDto(
    string? Title,
    string? Description,
    DateTimeOffset? DueDate,
    bool? IsBlocked,
    string? BlockReason,
    List<Guid>? LabelIds,
    DateTimeOffset? ExpectedUpdatedAt = null,
    bool ClearDueDate = false,
    string? WorkItemType = null,
    Guid? ParentCardId = null,
    bool ClearParent = false
);

public record MoveCardDto(
    Guid TargetColumnId,
    int TargetPosition
);

public record CardCaptureProvenanceDto(
    Guid CardId,
    Guid CaptureItemId,
    Guid ProposalId,
    ProposalStatus ProposalStatus,
    Guid? TriageRunId
);
