using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public enum ArtefactStoreResult
{
    Stored,
    UserInactive,
    BoardAccessDenied,
    QuotaExceeded
}

public interface ISourceArtefactRepository : IRepository<SourceArtefact>
{
    Task<SourceArtefact?> GetByIdForUserAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SourceArtefact>> GetByUserAsync(
        Guid userId,
        int limit = 500,
        int offset = 0,
        CancellationToken cancellationToken = default);

    Task<long> GetTotalByteSizeByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically rechecks the active user, board write authority, and byte quota,
    /// then persists metadata, blob, and the content-free audit entries.
    /// </summary>
    Task<ArtefactStoreResult> TryAddWithinQuotaAsync(
        SourceArtefact artefact,
        byte[] content,
        long quotaBytes,
        AuditLog auditLog,
        AuditLog? boardAuditLog,
        CancellationToken cancellationToken = default);

    Task<byte[]?> GetContentForUserAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch-loads blob content for the requested artefacts owned by <paramref name="userId"/>,
    /// keyed by artefact id, in a single query. Artefacts that do not exist, are not owned by the
    /// user, or have no blob are simply absent from the result (never surfaced across users).
    /// Callers must bound <paramref name="ids"/> so the IN-clause stays within SQLite's parameter
    /// limit (SQLITE_MAX_VARIABLE_NUMBER = 999); the buffered export path pages ids in chunks of 500.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, byte[]>> GetContentsForUserAsync(
        IReadOnlyCollection<Guid> ids,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<bool> CopyContentForUserAsync(
        Guid id,
        Guid userId,
        Stream destination,
        CancellationToken cancellationToken = default);

    Task<int> DeleteByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteWithAuditAsync(
        Guid id,
        Guid userId,
        AuditLog auditLog,
        AuditLog? boardAuditLog,
        CancellationToken cancellationToken = default);
}
