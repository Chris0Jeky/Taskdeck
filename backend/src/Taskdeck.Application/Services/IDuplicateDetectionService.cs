using Taskdeck.Application.DTOs;

namespace Taskdeck.Application.Services;

/// <summary>
/// Detects near-duplicate documents at ingest time. Uses vector similarity
/// when available, with FTS fallback. Calibrated with a precision-favoring
/// tradeoff to minimize false-positive duplicate flags.
/// </summary>
public interface IDuplicateDetectionService
{
    /// <summary>
    /// Checks whether the given content is a near-duplicate of an existing
    /// document owned by the specified user. Returns a detection result
    /// with a review cue when similarity exceeds the configured threshold.
    /// </summary>
    /// <param name="content">The text content to check for duplicates.</param>
    /// <param name="title">The title of the document being ingested.</param>
    /// <param name="userId">The owner to scope the duplicate search to.</param>
    /// <param name="boardId">Optional board scope for narrower matching.</param>
    /// <param name="excludeDocumentId">
    /// Optional document ID to exclude from matching (e.g. when updating
    /// an existing document, exclude itself).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<DuplicateDetectionResultDto> DetectAsync(
        string content,
        string title,
        Guid userId,
        Guid? boardId = null,
        Guid? excludeDocumentId = null,
        CancellationToken cancellationToken = default);
}
