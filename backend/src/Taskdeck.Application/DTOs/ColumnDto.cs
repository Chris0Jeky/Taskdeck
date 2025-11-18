namespace Taskdeck.Application.DTOs;

public record ColumnDto(
    Guid Id,
    Guid BoardId,
    string Name,
    int Position,
    int? WipLimit,
    int CardCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public record CreateColumnDto(
    Guid BoardId,
    string Name,
    int? Position,
    int? WipLimit
);

public record UpdateColumnDto(
    string? Name,
    int? Position,
    int? WipLimit
);
