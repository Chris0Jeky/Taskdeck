using System.ComponentModel.DataAnnotations;

namespace Taskdeck.Application.DTOs;

public record AnalyzeWorkspaceDto(Guid BoardId);
public record InsightActionDto([Required] string Action);
public record AnswerInsightDto([Required, MaxLength(8000)] string Text, [Required] string Status, [Required, MaxLength(8000)] string Evidence);
public record CreateWorkspaceMemoryDto(Guid BoardId, [Required, MaxLength(240)] string Title, [Required, MaxLength(8000)] string Text, [Required] string Status);
public record UpdateWorkspaceMemoryDto([Required, MaxLength(240)] string Title, [Required, MaxLength(8000)] string Text, [Required] string Status, int Revision);
public record ArchiveWorkspaceMemoryDto(bool Archived, int Revision);
public record WorkspaceMemoryRevisionDto(string Title, string Text, string Status, int Revision, bool Archived, DateTimeOffset RecordedAt);
public record WorkspaceMemoryDto(Guid Id, Guid BoardId, string Title, string Text, string OriginalText, string Status, bool Archived, int Revision, DateTimeOffset CreatedAt, IReadOnlyList<WorkspaceMemoryRevisionDto> History, string? OriginalEvidence, ThinkingAnswerSourceDto? ThinkingSource = null);
public record QuietInsightDto(Guid Id, Guid BoardId, Guid? CardId, Guid? MemoryId, string Rule, string Title, string Detail, string State, string Evidence, DateTimeOffset CheckedAt, DateTimeOffset? SnoozeUntil);
