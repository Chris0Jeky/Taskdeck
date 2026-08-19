using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.DTOs;

/// <summary>
/// A stored transcript returned to its owning user for evidence review.
/// <para>
/// <c>Text</c> is the canonical LF-normalized transcript exactly as persisted, so the
/// character offsets recorded on <see cref="ProvenanceEvidenceLinkDto"/> index directly
/// into it. It is returned whole (the domain caps transcripts at
/// <c>Transcript.MaxTextLength</c> characters); there is no paging.
/// </para>
/// </summary>
public sealed record TranscriptDto(
    Guid Id,
    Guid? BoardId,
    CaptureSource CaptureSource,
    string Text,
    IReadOnlyList<TranscriptSegmentDto> Segments,
    Guid? CreatedFromCaptureId,
    Guid? SourceArtefactId,
    DateTimeOffset CreatedAt);

/// <summary>
/// A line-indexed annotation within a transcript. Both bounds are zero-based and
/// inclusive against the LF-normalized text.
/// </summary>
public sealed record TranscriptSegmentDto(
    int StartLine,
    int EndLine,
    string? Speaker,
    long? TimestampMilliseconds);
