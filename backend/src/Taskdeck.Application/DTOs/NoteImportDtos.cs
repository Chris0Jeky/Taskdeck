namespace Taskdeck.Application.DTOs;

/// <summary>
/// Request to import a markdown file as one or more capture items.
/// The content is parsed, split into logical sections, and routed
/// through the standard capture pipeline.
/// </summary>
public sealed record MarkdownImportRequestDto(
    string FileName,
    string Content,
    Guid? BoardId = null);

/// <summary>
/// Request to import a web clip (URL + content snippet) as a capture item.
/// The content is routed through the standard capture pipeline with
/// source provenance preserved.
/// </summary>
public sealed record WebClipImportRequestDto(
    string Url,
    string Content,
    string? Title = null,
    Guid? BoardId = null);

/// <summary>
/// Result of a note-style import operation.
/// </summary>
public sealed record NoteImportResultDto(
    int ItemsCreated,
    IReadOnlyList<NoteImportItemResultDto> Items);

/// <summary>
/// Result for a single capture item created from a note import.
/// </summary>
public sealed record NoteImportItemResultDto(
    Guid CaptureItemId,
    string TextExcerpt,
    string SourceType,
    string? SourceRef);
