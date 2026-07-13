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
