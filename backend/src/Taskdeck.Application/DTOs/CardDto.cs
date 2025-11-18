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
    List<Guid>? LabelIds
);

public record MoveCardDto(
    Guid TargetColumnId,
    int TargetPosition
);
