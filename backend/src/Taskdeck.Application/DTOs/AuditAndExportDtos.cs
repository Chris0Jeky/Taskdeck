using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Entities;

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
    string ExportedBy,
    IReadOnlyList<ExportThinkingDeckDto>? ThinkingDecks = null,
    IReadOnlyList<CardDependency>? Dependencies = null);

// The envelope deliberately has no top-level board/name: older importers reject it
// instead of importing the cards while silently discarding their relationships.
public sealed record BoardExportEnvelope(string Format, int Version, ExportBoardDto Payload);

public sealed record ThinkingMaterialDto(int SchemaVersion, IReadOnlyList<ThinkingLayer> Layers);
public sealed record ExportThinkingDeckDto(Guid CardId, ThinkingMaterialDto Material);

public record ImportBoardDto(
    string Name,
    string? Description,
    IEnumerable<ImportColumnDto> Columns,
    IEnumerable<ImportCardDto> Cards,
    IEnumerable<ImportLabelDto> Labels,
    IReadOnlyList<CardDependency>? Dependencies = null,
    IReadOnlyDictionary<string, Guid?>? AssigneeMappings = null);

public record ImportSourceAssigneeDto(string SourceKey, string DisplayName);
public record ImportAssigneePreviewDto(string SourceKey, string DisplayName, int AffectedCardCount);
public record BoardImportPreviewDto(ImportBoardDto Board, int CardCount, int ColumnCount,
    IReadOnlyList<ImportAssigneePreviewDto> SourceAssignees, BoardParticipantDto Me);

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
    IEnumerable<string>? Labels,
    ThinkingMaterialDto? Thinking = null, Guid? SourceId = null, bool IsArchived = false, string WorkItemType = "Task", Guid? ParentCardId = null,
    IReadOnlyList<ImportSourceAssigneeDto>? SourceAssignees = null);

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
