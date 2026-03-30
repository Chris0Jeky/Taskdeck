namespace Taskdeck.Application.DTOs;

public record GlobalSearchResultDto(
    List<SearchBoardHitDto> Boards,
    List<SearchCardHitDto> Cards
);

public record SearchBoardHitDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsArchived
);

public record SearchCardHitDto(
    Guid Id,
    Guid BoardId,
    string BoardName,
    Guid ColumnId,
    string ColumnName,
    string Title,
    string? Description
);
