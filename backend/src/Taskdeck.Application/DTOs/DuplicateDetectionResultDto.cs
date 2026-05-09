namespace Taskdeck.Application.DTOs;

/// <summary>
/// Result of near-duplicate detection at ingest time.
/// Carries the most similar existing document and a similarity score
/// so the review UI can surface a "similar to existing" chip.
/// </summary>
public sealed record DuplicateDetectionResultDto(
    /// <summary>Whether a near-duplicate was found above the threshold.</summary>
    bool IsProbableDuplicate,

    /// <summary>
    /// Similarity score in [0.0, 1.0]. Only meaningful when
    /// <see cref="IsProbableDuplicate"/> is true or when the score is
    /// above the soft threshold for review hints.
    /// </summary>
    double SimilarityScore,

    /// <summary>The existing document ID that matched, if any.</summary>
    Guid? MatchedDocumentId,

    /// <summary>Title of the matched document, for display.</summary>
    string? MatchedDocumentTitle,

    /// <summary>
    /// Human-readable reason chip text, e.g. "similar to existing: API Review Notes".
    /// Null when no similarity detected.
    /// </summary>
    string? ReviewCue);
