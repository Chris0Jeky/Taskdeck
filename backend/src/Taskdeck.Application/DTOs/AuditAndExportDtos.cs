using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.DTOs;

// AuditLog DTOs
public record AuditLogDto(
    Guid Id,
    string EntityType,
    Guid EntityId,
    AuditAction Action,
    Guid? UserId,
    string? UserName,
    string? Changes,
    DateTimeOffset Timestamp);

// Export/Import DTOs
public record ExportBoardDto(
    BoardDto Board,
    IEnumerable<ColumnDto> Columns,
    IEnumerable<CardDto> Cards,
    IEnumerable<LabelDto> Labels,
    IEnumerable<BoardAccessDto> Accesses,
    DateTimeOffset ExportedAt,
    string ExportedBy);

public record ImportBoardDto(
    string Name,
    string? Description,
    IEnumerable<ImportColumnDto> Columns,
    IEnumerable<ImportCardDto> Cards,
    IEnumerable<ImportLabelDto> Labels);

public record ImportColumnDto(
    string Name,
    int Position,
    int? WipLimit);

public record ImportCardDto(
    string Title,
    string? Description,
    string ColumnName,
    int Position,
    DateTimeOffset? DueDate,
    IEnumerable<string>? Labels);

public record ImportLabelDto(
    string Name,
    string Color);

public record ImportResultDto(
    bool Success,
    Guid? BoardId,
    string? ErrorMessage,
    int ColumnsImported,
    int CardsImported,
    int LabelsImported);
