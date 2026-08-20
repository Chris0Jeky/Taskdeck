using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

/// <summary>
/// Reads a caller's own transcript so the Paper Review surface can display the
/// evidence span behind a proposal. Read-only: no board, proposal, or transcript
/// state is mutated here (GP INV-01).
/// </summary>
public sealed class TranscriptQueryService : ITranscriptQueryService
{
    private readonly ITranscriptRepository _transcripts;

    public TranscriptQueryService(ITranscriptRepository transcripts)
    {
        _transcripts = transcripts ?? throw new ArgumentNullException(nameof(transcripts));
    }

    public async Task<Result<TranscriptDto>> GetForUserAsync(
        Guid userId,
        Guid transcriptId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure<TranscriptDto>(ErrorCodes.ValidationError, "User ID cannot be empty");
        if (transcriptId == Guid.Empty)
            return Result.Failure<TranscriptDto>(ErrorCodes.ValidationError, "Transcript ID cannot be empty");

        // Ownership is part of the lookup predicate, so a transcript belonging to another
        // user is indistinguishable from one that does not exist. Never split this into a
        // fetch-then-compare: that reintroduces a cross-user existence oracle.
        var transcript = await _transcripts.GetByIdForUserAsync(transcriptId, userId, cancellationToken);

        return transcript is null
            ? Result.Failure<TranscriptDto>(ErrorCodes.NotFound, "Transcript not found")
            : Result.Success(Map(transcript));
    }

    internal static TranscriptDto Map(Transcript transcript) => new(
        transcript.Id,
        transcript.BoardId,
        transcript.CaptureSource,
        // Returned verbatim: evidence spans are character offsets into this exact
        // LF-normalized string, so any re-normalization here would shift them.
        transcript.Text,
        transcript.Segments
            .Select(segment => new TranscriptSegmentDto(
                segment.StartLine,
                segment.EndLine,
                segment.Speaker,
                segment.TimestampMilliseconds))
            .ToList()
            .AsReadOnly(),
        transcript.CreatedFromCaptureId,
        transcript.SourceArtefactId,
        transcript.CreatedAt);
}
