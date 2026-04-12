using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

/// <summary>
/// Handles note-style import (markdown files, web clips) by routing
/// imported content through the standard capture pipeline.
/// No direct board mutations — all content enters the capture → triage → proposal flow.
/// </summary>
public interface INoteImportService
{
    /// <summary>
    /// Parses a markdown file and creates capture items for each logical section.
    /// </summary>
    Task<Result<NoteImportResultDto>> ImportMarkdownAsync(
        Guid userId,
        MarkdownImportRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a capture item from a web clip (URL + content snippet).
    /// </summary>
    Task<Result<NoteImportResultDto>> ImportWebClipAsync(
        Guid userId,
        WebClipImportRequestDto request,
        CancellationToken cancellationToken = default);
}
