using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.DTOs;

public sealed record WorkspacePlanCardDto(Guid BoardId, Guid CardId, bool Available,
    string? Title, string? BoardName, string? ColumnName, DateTimeOffset? DueDate,
    bool IsBlocked, string? BlockReason);
public sealed record WorkspacePlanEntryDto(Guid BoardId, Guid CardId, DateOnly PlannedDate, bool Available,
    string? Title, string? BoardName, string? ColumnName, DateTimeOffset? DueDate,
    bool IsBlocked, string? BlockReason);
public sealed record WorkspacePlanFocusDto(Guid BoardId, Guid CardId, bool Available,
    string? Title, string? BoardName, string? ColumnName, DateTimeOffset? DueDate,
    bool IsBlocked, string? BlockReason, DateTimeOffset WorkedAt);
public sealed record WorkspacePlanDto(long Revision, IReadOnlyList<WorkspacePlanEntryDto> Entries, WorkspacePlanFocusDto? LastWorked);
public sealed record SaveWorkspacePlanDto(long ExpectedRevision, IReadOnlyList<PersonalPlanEntry> Entries);
public sealed record FocusWorkspacePlanDto(long ExpectedRevision, Guid BoardId, Guid CardId);
