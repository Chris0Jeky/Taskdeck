using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public sealed class ArtefactExtractionRepository : IArtefactExtractionRepository
{
    private const int MaxPageSize = 50;
    private const int EstimatedJsonOverheadCharacters = 512;
    private readonly TaskdeckDbContext _context;

    public ArtefactExtractionRepository(TaskdeckDbContext context)
    {
        _context = context;
    }

    public Task<ArtefactExtractionStoreResult> TryAddForUserAsync(
        ArtefactExtraction extraction,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return ExecuteInImmediateWriteTransactionAsync(async () =>
        {
            var userIsActive = await _context.Users.AnyAsync(
                user => user.Id == userId && user.IsActive,
                cancellationToken);
            if (!userIsActive)
                return ArtefactExtractionStoreResult.UserInactive;

            var sourceIsOwned = await _context.SourceArtefacts.AnyAsync(
                artefact => artefact.Id == extraction.SourceArtefactId && artefact.UserId == userId,
                cancellationToken);
            if (!sourceIsOwned)
                return ArtefactExtractionStoreResult.SourceArtefactUnavailable;

            await _context.ArtefactExtractions.AddAsync(extraction, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            return ArtefactExtractionStoreResult.Stored;
        }, cancellationToken);
    }

    public async Task<ArtefactExtraction?> GetLatestForArtefactForUserAsync(
        Guid sourceArtefactId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (_context.Database.IsSqlite())
        {
            return await _context.ArtefactExtractions
                .FromSqlInterpolated($"""
                    SELECT extraction.*
                    FROM ArtefactExtractions AS extraction
                    INNER JOIN SourceArtefacts AS artefact
                        ON artefact.Id = extraction.SourceArtefactId
                    WHERE extraction.SourceArtefactId = {sourceArtefactId}
                        AND artefact.UserId = {userId}
                    ORDER BY extraction.CreatedAt DESC, extraction.Id DESC
                    LIMIT 1
                    """)
                .AsNoTracking()
                .SingleOrDefaultAsync(cancellationToken);
        }

        return await (
                from extraction in _context.ArtefactExtractions.AsNoTracking()
                join artefact in _context.SourceArtefacts.AsNoTracking()
                    on extraction.SourceArtefactId equals artefact.Id
                where extraction.SourceArtefactId == sourceArtefactId && artefact.UserId == userId
                orderby extraction.CreatedAt descending, extraction.Id descending
                select extraction)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ArtefactExtraction>> GetByArtefactForUserAsync(
        Guid sourceArtefactId,
        Guid userId,
        int limit = MaxPageSize,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var boundedLimit = Math.Clamp(limit, 1, MaxPageSize);
        var boundedOffset = Math.Max(offset, 0);

        if (_context.Database.IsSqlite())
        {
            return await _context.ArtefactExtractions
                .FromSqlInterpolated($"""
                    SELECT extraction.*
                    FROM ArtefactExtractions AS extraction
                    INNER JOIN SourceArtefacts AS artefact
                        ON artefact.Id = extraction.SourceArtefactId
                    WHERE extraction.SourceArtefactId = {sourceArtefactId}
                        AND artefact.UserId = {userId}
                    ORDER BY extraction.CreatedAt ASC, extraction.Id ASC
                    LIMIT {boundedLimit} OFFSET {boundedOffset}
                    """)
                .AsNoTracking()
                .ToListAsync(cancellationToken);
        }

        return await (
                from extraction in _context.ArtefactExtractions.AsNoTracking()
                join artefact in _context.SourceArtefacts.AsNoTracking()
                    on extraction.SourceArtefactId equals artefact.Id
                where extraction.SourceArtefactId == sourceArtefactId && artefact.UserId == userId
                orderby extraction.CreatedAt, extraction.Id
                select extraction)
            .Skip(boundedOffset)
            .Take(boundedLimit)
            .ToListAsync(cancellationToken);
    }

    public async Task<long> GetTotalTextLengthByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var total = await (
                from extraction in _context.ArtefactExtractions
                join artefact in _context.SourceArtefacts
                    on extraction.SourceArtefactId equals artefact.Id
                where artefact.UserId == userId
                select (long?)extraction.TextLength)
            .SumAsync(cancellationToken);
        return total ?? 0L;
    }

    public async Task<long> GetEstimatedSerializedBytesByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var serializedCharacters = await (
                from extraction in _context.ArtefactExtractions
                join artefact in _context.SourceArtefacts
                    on extraction.SourceArtefactId equals artefact.Id
                where artefact.UserId == userId
                select (long?)(
                    extraction.TextLength +
                    extraction.WarningsJson.Length +
                    extraction.ExtractorName.Length +
                    extraction.ExtractorVersion.Length +
                    EstimatedJsonOverheadCharacters))
            .SumAsync(cancellationToken) ?? 0L;

        // A UTF-16 code unit can expand to six ASCII bytes when JSON escaped.
        return serializedCharacters > long.MaxValue / 6
            ? long.MaxValue
            : serializedCharacters * 6;
    }

    private async Task<T> ExecuteInImmediateWriteTransactionAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (_context.Database.CurrentTransaction is not null)
            return await action();

        if (!_context.Database.IsSqlite())
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            var result = await action();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }

        await _context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = (SqliteConnection)_context.Database.GetDbConnection();
            await using var sqliteTransaction = connection.BeginTransaction(deferred: false);
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
