using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

/// <summary>Explicit human transcript writes. This does not invoke a speech service or migrate legacy transcripts.</summary>
public interface IManualRepresentationStore : IRepresentationStore
{
    Task<Representation?> HeaderAsync(Guid id, Guid actorId, CancellationToken ct);
    Task StageTranscriptAsync(Guid actorId, Representation header, Transcript payload, RepresentationSupersession? supersession, CancellationToken ct);
    Task<Transcript?> TranscriptAsync(Guid id, Guid actorId, CancellationToken ct);
}
