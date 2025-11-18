namespace Taskdeck.Application.DTOs;

public record BoardDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public record BoardDetailDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    List<ColumnDto> Columns
);

public record CreateBoardDto(
    string Name,
    string? Description
);

public record UpdateBoardDto(
    string? Name,
    string? Description,
    bool? IsArchived
);
