using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Interfaces;

/// <summary>
/// Read-only access to a caller's own stored transcripts, so the Review surface can
/// show the evidence span behind a proposal. This is a read surface only — it never
/// mutates a transcript, a proposal, or a board.
/// </summary>
public interface ITranscriptQueryService
{
    /// <summary>
    /// Returns the transcript identified by <paramref name="transcriptId"/> when it is owned
    /// by <paramref name="userId"/>.
    /// <para>
    /// A transcript owned by another user is reported as <c>NotFound</c>, identically to a
    /// transcript that does not exist, so the endpoint cannot be used as a cross-user
    /// existence oracle.
    /// </para>
    /// </summary>
    Task<Result<TranscriptDto>> GetForUserAsync(
        Guid userId,
        Guid transcriptId,
        CancellationToken cancellationToken = default);
}
