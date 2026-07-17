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

    /// <summary>
    /// Upper bound on artefact ids accepted by <see cref="GetByArtefactsForUserAsync"/> in one
    /// call, applied to the RAW input count (before de-duplication). Each id becomes one SQLite
    /// bind parameter, so the worst case is 900 ids + the userId = 901 bind parameters — under the
    /// legacy SQLITE_MAX_VARIABLE_NUMBER default of 999 (a deliberate ~10% margin; modern bundled
    /// SQLite defaults to 32766, so this cap is conservative defense-in-depth). Mirrors
    /// <c>SourceArtefactRepository.MaxBatchIdCount</c>. Coupled constraint:
    /// <c>DataExportService.StreamPageSize</c> (currently 500) must stay &lt;= this cap — raising
    /// that chunk size past 900 makes the buffered export throw at runtime.
    /// </summary>
    private const int MaxBatchIdCount = 900;

    private static readonly IReadOnlyDictionary<Guid, IReadOnlyList<ArtefactExtraction>> EmptyHistoryMap =
        new Dictionary<Guid, IReadOnlyList<ArtefactExtraction>>();

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
        // Push the "latest" selection (ORDER BY + LIMIT 1) into raw SQL under SQLite to
        // keep an explicit, deterministic in-database ordering and to match the
        // established IsSqlite()/FromSqlInterpolated convention shared by
        // ChatSessionRepository / LlmQueueRepository / AuditLogRepository. The SQLite
        // provider can translate the equivalent LINQ (it orders the stored TEXT, which
        // is chronologically correct for the all-UTC CreatedAt values here), but the raw
        // form keeps ordering consistent with how the rest of the codebase resolves these
        // reads on SQLite. Other providers use the LINQ branch below. Coverage:
        // ArtefactExtractionPersistenceTests.Queries_ReturnDeterministicHistoryWithinUserBoundary.
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

        // Push ordering + LIMIT/OFFSET into raw SQL under SQLite for an explicit,
        // deterministic in-database pagination, matching the IsSqlite()/FromSqlInterpolated
        // convention used across ChatSessionRepository / LlmQueueRepository /
        // AuditLogRepository (same rationale as GetLatestForArtefactForUserAsync above:
        // the SQLite provider can translate the equivalent LINQ, but the raw form keeps
        // ordering consistent with the rest of the codebase's SQLite reads). Other
        // providers use the LINQ branch below. Coverage:
        // ArtefactExtractionPersistenceTests.Queries_ReturnDeterministicHistoryWithinUserBoundary.
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

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ArtefactExtraction>>> GetByArtefactsForUserAsync(
        IReadOnlyCollection<Guid> sourceArtefactIds,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (sourceArtefactIds.Count == 0)
            return EmptyHistoryMap;

        if (sourceArtefactIds.Count > MaxBatchIdCount)
            throw new ArgumentException(
                $"Cannot batch more than {MaxBatchIdCount} artefact ids in one query; page the ids.",
                nameof(sourceArtefactIds));

        // De-duplicate to keep the IN-clause parameter footprint minimal; the caller bounds the
        // set size (<= 500) and the guard above caps it.
        var idList = sourceArtefactIds.Distinct().ToList();

        List<ArtefactExtraction> rows;
        if (_context.Database.IsSqlite())
        {
            // Mirror GetByArtefactForUserAsync's SQLite raw-SQL ordering (CreatedAt ASC, Id ASC over
            // the stored TEXT) so each artefact's batch group is byte-for-byte identical to the
            // former per-artefact page reads — including the Guid tiebreak, which orders by TEXT
            // here (not .NET Guid comparison). The IN-clause is fully parameterised: one param per
            // id plus the userId, at most 901 bind parameters under the MaxBatchIdCount cap (see
            // its doc for the margin against SQLite's parameter limits). Same user-scoped join as
            // the per-artefact read, so a foreign artefact id can never surface history.
            var placeholders = new string[idList.Count];
            var parameters = new List<object>(idList.Count + 1);
            for (var i = 0; i < idList.Count; i++)
            {
                placeholders[i] = $"@id{i}";
                parameters.Add(new SqliteParameter($"@id{i}", idList[i]));
            }
            parameters.Add(new SqliteParameter("@userId", userId));

            var sql = $"""
                SELECT extraction.*
                FROM ArtefactExtractions AS extraction
                INNER JOIN SourceArtefacts AS artefact
                    ON artefact.Id = extraction.SourceArtefactId
                WHERE extraction.SourceArtefactId IN ({string.Join(", ", placeholders)})
                    AND artefact.UserId = @userId
                ORDER BY extraction.SourceArtefactId ASC, extraction.CreatedAt ASC, extraction.Id ASC
                """;

            rows = await _context.ArtefactExtractions
                .FromSqlRaw(sql, parameters.ToArray())
                .AsNoTracking()
                .ToListAsync(cancellationToken);
        }
        else
        {
            // Non-SQLite providers: the Id tiebreak uses the provider's native Guid ordering, which
            // can differ from SQLite's TEXT ordering above and is deliberately unpinned by tests
            // (same pre-existing pattern as GetByArtefactForUserAsync's LINQ branch). Byte-for-byte
            // export parity with the former per-artefact reads is guaranteed only on SQLite.
            rows = await (
                    from extraction in _context.ArtefactExtractions.AsNoTracking()
                    join artefact in _context.SourceArtefacts.AsNoTracking()
                        on extraction.SourceArtefactId equals artefact.Id
                    where artefact.UserId == userId && idList.Contains(extraction.SourceArtefactId)
                    orderby extraction.SourceArtefactId, extraction.CreatedAt, extraction.Id
                    select extraction)
                .ToListAsync(cancellationToken);
        }

        // Rows arrive ordered by (SourceArtefactId, CreatedAt, Id); appending in encounter order
        // preserves each artefact's CreatedAt/Id ordering within its group.
        var grouped = new Dictionary<Guid, List<ArtefactExtraction>>();
        foreach (var extraction in rows)
        {
            if (!grouped.TryGetValue(extraction.SourceArtefactId, out var list))
            {
                list = new List<ArtefactExtraction>();
                grouped[extraction.SourceArtefactId] = list;
            }

            list.Add(extraction);
        }

        var map = new Dictionary<Guid, IReadOnlyList<ArtefactExtraction>>(grouped.Count);
        foreach (var entry in grouped)
            map[entry.Key] = entry.Value;
        return map;
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
