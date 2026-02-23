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
    DateTimeOffset UpdatedAt
);

public record CreateCardDto(
    Guid BoardId,
    Guid ColumnId,
    string Title,
    string? Description,
    DateTimeOffset? DueDate,
    List<Guid>? LabelIds
);

public record UpdateCardDto(
    string? Title,
    string? Description,
    DateTimeOffset? DueDate,
    bool? IsBlocked,
    string? BlockReason,
    List<Guid>? LabelIds,
    DateTimeOffset? ExpectedUpdatedAt = null
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
