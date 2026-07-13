using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

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
    /// Atomically checks the user's byte quota and persists metadata, blob, and
    /// the content-free audit entry. Returns false when the quota would be exceeded.
    /// </summary>
    Task<bool> TryAddWithinQuotaAsync(
        SourceArtefact artefact,
        byte[] content,
        long quotaBytes,
        AuditLog auditLog,
        CancellationToken cancellationToken = default);

    Task<byte[]?> GetContentForUserAsync(
        Guid id,
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
        CancellationToken cancellationToken = default);
}
