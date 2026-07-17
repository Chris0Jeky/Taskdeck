using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public enum ArtefactExtractionStoreResult
{
    Stored,
    UserInactive,
    SourceArtefactUnavailable
}

public interface IArtefactExtractionRepository
{
    /// <summary>
    /// Rechecks the active user and source ownership at commit time before
    /// appending immutable extraction history.
    /// </summary>
    Task<ArtefactExtractionStoreResult> TryAddForUserAsync(
        ArtefactExtraction extraction,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<ArtefactExtraction?> GetLatestForArtefactForUserAsync(
        Guid sourceArtefactId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ArtefactExtraction>> GetByArtefactForUserAsync(
        Guid sourceArtefactId,
        Guid userId,
        int limit = 50,
        int offset = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch-loads the complete extraction history for the requested artefacts owned by
    /// <paramref name="userId"/>, keyed by source-artefact id, in a single user-scoped query.
    /// Each artefact's list is ordered <c>CreatedAt ASC, Id ASC</c> — identical to the per-artefact
    /// <see cref="GetByArtefactForUserAsync"/> ordering — so a caller may concatenate the groups for
    /// a byte-for-byte identical export. Artefacts with no extractions, or not owned by the user,
    /// are simply absent from the result (never surfaced across users). Mirrors the batching
    /// convention of <see cref="ISourceArtefactRepository.GetContentsForUserAsync"/>: callers must
    /// bound <paramref name="sourceArtefactIds"/> so the IN-clause stays within SQLite's parameter
    /// limit (SQLITE_MAX_VARIABLE_NUMBER = 999); the buffered export pages ids in chunks of 500.
    /// Passing more than the implementation's batch cap throws <see cref="ArgumentException"/>.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<ArtefactExtraction>>> GetByArtefactsForUserAsync(
        IReadOnlyCollection<Guid> sourceArtefactIds,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<long> GetTotalTextLengthByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a conservative upper bound for the JSON bytes needed to export
    /// all extraction records owned by the user.
    /// </summary>
    Task<long> GetEstimatedSerializedBytesByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
