using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using System.Data;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

/// <summary>
/// Keeps every metadata query on SourceArtefacts. ArtefactBlobs is accessed only
/// by explicit content/export calls, so ordinary reads cannot materialize bytes.
/// </summary>
public sealed class SourceArtefactRepository : Repository<SourceArtefact>, ISourceArtefactRepository
{
    public SourceArtefactRepository(TaskdeckDbContext context) : base(context)
    {
    }

    public Task<SourceArtefact?> GetByIdForUserAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken = default)
        => _dbSet.AsNoTracking().FirstOrDefaultAsync(
            a => a.Id == id && a.UserId == userId,
            cancellationToken);

    public async Task<IReadOnlyList<SourceArtefact>> GetByUserAsync(
        Guid userId,
        int limit = 500,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var boundedLimit = Math.Clamp(limit, 1, 500);
        var boundedOffset = Math.Max(offset, 0);

        // DateTimeOffset ordering is not translated by SQLite. Ordering by Id is
        // deterministic for export paging; no blob join is permitted here.
        return await _dbSet
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderBy(a => a.Id)
            .Skip(boundedOffset)
            .Take(boundedLimit)
            .ToListAsync(cancellationToken);
    }

    public async Task<long> GetTotalByteSizeByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var total = await _dbSet
            .Where(a => a.UserId == userId)
            .SumAsync(a => (long?)a.ByteSize, cancellationToken);
        return total ?? 0L;
    }

    public Task<bool> TryAddWithinQuotaAsync(
        SourceArtefact artefact,
        byte[] content,
        long quotaBytes,
        AuditLog auditLog,
        CancellationToken cancellationToken = default)
    {
        return ExecuteInImmediateWriteTransactionAsync(async () =>
        {
            var usedBytes = await GetTotalByteSizeByUserAsync(artefact.UserId, cancellationToken);
            if (usedBytes > quotaBytes - artefact.ByteSize)
                return false;

            await _dbSet.AddAsync(artefact, cancellationToken);
            await _context.Set<ArtefactBlob>().AddAsync(
                new ArtefactBlob(artefact.Id, content),
                cancellationToken);
            await _context.AuditLogs.AddAsync(auditLog, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);
    }

    public async Task<byte[]?> GetContentForUserAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await (
            from artefact in _context.SourceArtefacts.AsNoTracking()
            join blob in _context.ArtefactBlobs.AsNoTracking()
                on artefact.Id equals blob.SourceArtefactId
            where artefact.Id == id && artefact.UserId == userId
            select blob.Content)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> CopyContentForUserAsync(
        Guid id,
        Guid userId,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        await _context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = _context.Database.GetDbConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT b.Content
                FROM ArtefactBlobs AS b
                INNER JOIN SourceArtefacts AS a ON a.Id = b.SourceArtefactId
                WHERE a.Id = @id AND a.UserId = @userId
                LIMIT 1;
                """;
            var idParameter = command.CreateParameter();
            idParameter.ParameterName = "@id";
            idParameter.Value = id;
            command.Parameters.Add(idParameter);
            var userIdParameter = command.CreateParameter();
            userIdParameter.ParameterName = "@userId";
            userIdParameter.Value = userId;
            command.Parameters.Add(userIdParameter);

            await using var reader = await command.ExecuteReaderAsync(
                CommandBehavior.SequentialAccess | CommandBehavior.SingleRow,
                cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return false;

            await using var source = reader.GetStream(0);
            await source.CopyToAsync(destination, 64 * 1024, cancellationToken);
            return true;
        }
        finally
        {
            await _context.Database.CloseConnectionAsync();
        }
    }

    public async Task<int> DeleteByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var ids = _dbSet.Where(a => a.UserId == userId).Select(a => a.Id);
        await _context.ArtefactBlobs
            .Where(blob => ids.Contains(blob.SourceArtefactId))
            .ExecuteDeleteAsync(cancellationToken);
        return await _dbSet
            .Where(a => a.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public Task<bool> DeleteWithAuditAsync(
        Guid id,
        Guid userId,
        AuditLog auditLog,
        CancellationToken cancellationToken = default)
    {
        return ExecuteInImmediateWriteTransactionAsync(async () =>
        {
            var artefact = await _dbSet.FirstOrDefaultAsync(
                a => a.Id == id && a.UserId == userId,
                cancellationToken);
            if (artefact is null)
                return false;

            _dbSet.Remove(artefact);
            await _context.AuditLogs.AddAsync(auditLog, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);
    }

    private async Task<T> ExecuteInImmediateWriteTransactionAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (_context.Database.CurrentTransaction is not null)
            return await action();

        if (!_context.Database.IsSqlite())
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            var result = await action();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }

        await _context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = (SqliteConnection)_context.Database.GetDbConnection();
            var sqliteTransaction = connection.BeginTransaction(deferred: false);
            await using var transaction = await _context.Database.UseTransactionAsync(
                sqliteTransaction,
                cancellationToken)
                ?? throw new InvalidOperationException("Could not enlist the SQLite write transaction.");
            try
            {
                var result = await action();
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
        finally
        {
            await _context.Database.CloseConnectionAsync();
        }
    }
}
