namespace Taskdeck.Application.DTOs;

public record LabelDto(
    Guid Id,
    Guid BoardId,
    string Name,
    string ColorHex,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public record CreateLabelDto(
    Guid BoardId,
    string Name,
    string ColorHex
);

public record UpdateLabelDto(
    string? Name,
    string? ColorHex
);
