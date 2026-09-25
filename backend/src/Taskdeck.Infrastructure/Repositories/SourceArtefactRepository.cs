using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using System.Data;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

/// <summary>
/// Keeps metadata queries on SourceArtefacts. New content uses owner-scoped byte-store
/// references; legacy ArtefactBlobs are accessed only by explicit content/export calls.
/// </summary>
public sealed class SourceArtefactRepository : Repository<SourceArtefact>, ISourceArtefactRepository
{
    private readonly IBlobStore _blobStore;
    private static readonly IReadOnlyDictionary<Guid, byte[]> EmptyContentMap =
        new Dictionary<Guid, byte[]>();

    /// <summary>
    /// Upper bound on ids accepted by <see cref="GetContentsForUserAsync"/> in one call. EF Core 8
    /// parameterises the id set via <c>json_each</c> (a single SQLite parameter), so this is
    /// defense-in-depth against a future translation/provider change reintroducing per-id
    /// parameters — it keeps the worst case well under SQLITE_MAX_VARIABLE_NUMBER (999). Callers
    /// page in chunks of 500, comfortably below this bound.
    /// </summary>
    private const int MaxBatchIdCount = 900;

    public SourceArtefactRepository(TaskdeckDbContext context, IBlobStore blobStore) : base(context)
    {
        _blobStore = blobStore;
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

    public Task<ArtefactStoreResult> TryAddWithinQuotaAsync(
        SourceArtefact artefact,
        byte[] content,
        long quotaBytes,
        AuditLog auditLog,
        AuditLog? boardAuditLog,
        CancellationToken cancellationToken = default)
    {
        return ExecuteInImmediateWriteTransactionAsync(async () =>
        {
            var userIsActive = await _context.Users.AnyAsync(
                user => user.Id == artefact.UserId && user.IsActive,
                cancellationToken);
            if (!userIsActive)
                return ArtefactStoreResult.UserInactive;

            if (artefact.BoardId.HasValue)
            {
                var boardId = artefact.BoardId.Value;
                var hasEditorAccess = await _context.Boards.AnyAsync(
                        board => board.Id == boardId && board.OwnerId == artefact.UserId,
                        cancellationToken)
                    || await _context.BoardAccesses.AnyAsync(
                        access => access.BoardId == boardId &&
                                  access.UserId == artefact.UserId &&
                                  access.Role <= Taskdeck.Domain.Enums.UserRole.Editor,
                        cancellationToken);
                if (!hasEditorAccess)
                    return ArtefactStoreResult.BoardAccessDenied;
            }

            if (await ExceedsArtefactQuotaAsync(
                    artefact.UserId, artefact.ByteSize, quotaBytes, null, cancellationToken))
                return ArtefactStoreResult.QuotaExceeded;

            BlobReference reference;
            try
            {
                await using var source = new MemoryStream(content, writable: false);
                reference = await _blobStore.AcquireAsync(
                    new BlobAcquisition(artefact.UserId, ToModality(artefact.Kind), content.LongLength,
                        nameof(SourceArtefact), artefact.Id), source, cancellationToken);
            }
            catch (DomainException error) when (error.ErrorCode == ErrorCodes.PayloadTooLarge)
            {
                return ArtefactStoreResult.QuotaExceeded;
            }
            artefact.AttachBlobReference(reference.ReferenceId);
            await _dbSet.AddAsync(artefact, cancellationToken);
            await _context.AuditLogs.AddAsync(auditLog, cancellationToken);
            if (boardAuditLog is not null)
                await _context.AuditLogs.AddAsync(boardAuditLog, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            return ArtefactStoreResult.Stored;
        }, cancellationToken);
    }

    public async Task<StreamingArtefactStoreOutcome> TryAddStreamWithinQuotaAsync(
        StreamingArtefactWrite write,
        long quotaBytes,
        CancellationToken cancellationToken = default)
    {
        if (_context.Database.CurrentTransaction is not null)
            throw new InvalidOperationException(
                "Streaming source uploads require their own short reservation and finalization transactions.");
        var acquisition = new BlobAcquisition(write.UserId, ToModality(write.Kind),
            write.ExpectedByteSize, nameof(SourceArtefact), write.ArtefactId);
        var reservation = await ExecuteInImmediateWriteTransactionAsync(async () =>
        {
            var access = await CheckStreamWriteAccessAsync(write, cancellationToken);
            if (access != ArtefactStoreResult.Stored)
                return (Result: access, ReservationId: (Guid?)null);
            if (await ExceedsArtefactQuotaAsync(write.UserId, write.ExpectedByteSize,
                    quotaBytes, null, cancellationToken))
                return (Result: ArtefactStoreResult.QuotaExceeded, ReservationId: (Guid?)null);
            try
            {
                // Reserve under a short write lock, then release it before reading Request.Body.
                var id = await _blobStore.ReserveAsync(
                    acquisition, DateTime.UtcNow.AddMinutes(5), cancellationToken);
                return (Result: ArtefactStoreResult.Stored, ReservationId: (Guid?)id);
            }
            catch (DomainException error) when (error.ErrorCode == ErrorCodes.PayloadTooLarge)
            {
                return (Result: ArtefactStoreResult.QuotaExceeded, ReservationId: (Guid?)null);
            }
        }, cancellationToken);

        if (reservation.ReservationId is not Guid reservationId)
            return new StreamingArtefactStoreOutcome(reservation.Result, null);

        try
        {
            await using var spool = CreateUploadSpool(reservationId);
            var buffer = new byte[64 * 1024];
            long received = 0;
            while (true)
            {
                var requested = received < write.ExpectedByteSize
                    ? (int)Math.Min(buffer.Length, write.ExpectedByteSize - received)
                    : 1;
                var count = await write.Content.ReadAsync(buffer.AsMemory(0, requested), cancellationToken);
                if (count == 0) break;
                received += count;
                if (received > write.ExpectedByteSize)
                    throw new DomainException(ErrorCodes.PayloadTooLarge, "The upload exceeded its declared size.");
                await spool.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            }
            if (received != write.ExpectedByteSize)
                throw new DomainException(ErrorCodes.ValidationError,
                    "The upload ended before its declared size. Retry with the original file.");

            await spool.FlushAsync(cancellationToken);
            spool.Position = 0;
            return await ExecuteInImmediateWriteTransactionAsync(async () =>
            {
                var activeReservation = await _context.StoredBlobReservations.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == reservationId && x.OwnerUserId == write.UserId &&
                        x.Modality == acquisition.AssetModality && x.ByteSize == write.ExpectedByteSize &&
                        x.ReferrerId == write.ArtefactId && x.ExpiresAtUtc > DateTime.UtcNow,
                        cancellationToken);
                if (activeReservation is null)
                    return new StreamingArtefactStoreOutcome(ArtefactStoreResult.QuotaExceeded, null);
                var access = await CheckStreamWriteAccessAsync(write, cancellationToken);
                if (access != ArtefactStoreResult.Stored)
                    return new StreamingArtefactStoreOutcome(access, null);
                if (await ExceedsArtefactQuotaAsync(write.UserId, write.ExpectedByteSize,
                        quotaBytes, reservationId, cancellationToken))
                    return new StreamingArtefactStoreOutcome(ArtefactStoreResult.QuotaExceeded, null);

                if (!await _blobStore.ReleaseReservationAsync(reservationId, write.UserId, cancellationToken))
                    throw new InvalidOperationException("The reserved source upload disappeared during finalization.");
                BlobReference reference;
                try
                {
                    reference = await _blobStore.AcquireAsync(acquisition, spool, cancellationToken);
                }
                catch (DomainException error) when (error.ErrorCode == ErrorCodes.PayloadTooLarge)
                {
                    return new StreamingArtefactStoreOutcome(ArtefactStoreResult.QuotaExceeded, null);
                }

                var artefact = new SourceArtefact(write.ArtefactId, write.UserId, write.Kind,
                    write.MimeType, write.FileName, reference.ByteSize, reference.ContentHash,
                    CaptureSource.Import, write.BoardId, createdFromCaptureId: write.CreatedFromCaptureId);
                artefact.AttachBlobReference(reference.ReferenceId);
                await _dbSet.AddAsync(artefact, cancellationToken);
                await _context.AuditLogs.AddAsync(new AuditLog(
                    "SourceArtefact", artefact.Id, AuditAction.Created, write.UserId,
                    $"kind={artefact.Kind}; bytes={artefact.ByteSize}"), cancellationToken);
                if (artefact.BoardId.HasValue)
                    await _context.AuditLogs.AddAsync(new AuditLog(
                        "SourceArtefact", artefact.BoardId.Value, AuditAction.Created, write.UserId,
                        $"artefactId={artefact.Id}; kind={artefact.Kind}; bytes={artefact.ByteSize}"), cancellationToken);
                await _context.SaveChangesAsync(cancellationToken);
                return new StreamingArtefactStoreOutcome(ArtefactStoreResult.Stored, artefact);
            }, cancellationToken);
        }
        finally
        {
            // DeleteOnClose removes the private spool on normal completion or process death.
            // Cancellation still releases the durable quota claim with an independent token.
            await ExecuteInImmediateWriteTransactionAsync(async () =>
            {
                await _blobStore.ReleaseReservationAsync(reservationId, write.UserId, CancellationToken.None);
                return true;
            }, CancellationToken.None);
        }
    }

    private async Task<ArtefactStoreResult> CheckStreamWriteAccessAsync(
        StreamingArtefactWrite write, CancellationToken cancellationToken)
    {
        if (!await _context.Users.AnyAsync(
                user => user.Id == write.UserId && user.IsActive, cancellationToken))
            return ArtefactStoreResult.UserInactive;
        if (!write.BoardId.HasValue) return ArtefactStoreResult.Stored;
        var boardId = write.BoardId.Value;
        var hasEditorAccess = await _context.Boards.AnyAsync(
                board => board.Id == boardId && board.OwnerId == write.UserId, cancellationToken)
            || await _context.BoardAccesses.AnyAsync(
                access => access.BoardId == boardId && access.UserId == write.UserId &&
                          access.Role <= UserRole.Editor, cancellationToken);
        return hasEditorAccess ? ArtefactStoreResult.Stored : ArtefactStoreResult.BoardAccessDenied;
    }

    private async Task<bool> ExceedsArtefactQuotaAsync(
        Guid userId, long incomingBytes, long quotaBytes, Guid? replacingReservationId,
        CancellationToken cancellationToken)
    {
        if (incomingBytes > quotaBytes) return true;
        var usedBytes = await GetTotalByteSizeByUserAsync(userId, cancellationToken);
        if (usedBytes > quotaBytes - incomingBytes) return true;
        var now = DateTime.UtcNow;
        var reserved = await _context.StoredBlobReservations
            .Where(x => x.OwnerUserId == userId && x.ExpiresAtUtc > now &&
                (!replacingReservationId.HasValue || x.Id != replacingReservationId.Value))
            .SumAsync(x => x.ByteSize, cancellationToken);
        return reserved > quotaBytes - incomingBytes - usedBytes;
    }

    private static FileStream CreateUploadSpool(Guid reservationId)
    {
        var path = Path.Combine(Path.GetTempPath(), $"taskdeck-upload-{reservationId:N}.tmp");
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
            BufferSize = 64 * 1024,
            Options = FileOptions.Asynchronous | FileOptions.DeleteOnClose
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return new FileStream(path, options);
    }

    public async Task<byte[]?> GetContentForUserAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var referenceId = await _dbSet.AsNoTracking()
            .Where(a => a.Id == id && a.UserId == userId)
            .Select(a => a.BlobReferenceId)
            .SingleOrDefaultAsync(cancellationToken);
        if (referenceId.HasValue)
        {
            await using var source = await _blobStore.OpenReferenceReadAsync(referenceId.Value, userId, cancellationToken);
            if (source is null) return null;
            using var output = new MemoryStream();
            await source.CopyToAsync(output, cancellationToken);
            return output.ToArray();
        }
        return await (
            from artefact in _context.SourceArtefacts.AsNoTracking()
            join blob in _context.ArtefactBlobs.AsNoTracking()
                on artefact.Id equals blob.SourceArtefactId
            where artefact.Id == id && artefact.UserId == userId
            select blob.Content)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, byte[]>> GetContentsForUserAsync(
        IReadOnlyCollection<Guid> ids,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
            return EmptyContentMap;

        if (ids.Count > MaxBatchIdCount)
            throw new ArgumentException(
                $"Cannot batch more than {MaxBatchIdCount} artefact ids in one query; page the ids.",
                nameof(ids));

        // De-duplicate to keep the IN-clause parameter footprint minimal; the caller bounds the
        // set size (<= 500) and the guard above caps it. Same user-scoped blob join as
        // GetContentForUserAsync, so a foreign artefact id can never surface content.
        var idList = ids.Distinct().ToList();

        var rows = await (
            from artefact in _context.SourceArtefacts.AsNoTracking()
            join blob in _context.ArtefactBlobs.AsNoTracking()
                on artefact.Id equals blob.SourceArtefactId
            where artefact.UserId == userId && idList.Contains(artefact.Id)
            select new { artefact.Id, blob.Content })
            .ToListAsync(cancellationToken);

        var map = new Dictionary<Guid, byte[]>(rows.Count);
        foreach (var row in rows)
            map[row.Id] = row.Content;

        var references = await _dbSet.AsNoTracking()
            .Where(a => a.UserId == userId && idList.Contains(a.Id) && a.BlobReferenceId != null)
            .Select(a => new { a.Id, a.BlobReferenceId })
            .ToListAsync(cancellationToken);
        foreach (var artefact in references)
        {
            await using var source = await _blobStore.OpenReferenceReadAsync(
                artefact.BlobReferenceId!.Value, userId, cancellationToken);
            if (source is null) continue;
            using var output = new MemoryStream();
            await source.CopyToAsync(output, cancellationToken);
            map[artefact.Id] = output.ToArray();
        }
        return map;
    }

    public async Task<bool> CopyContentForUserAsync(
        Guid id,
        Guid userId,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        var referenceId = await _dbSet.AsNoTracking()
            .Where(a => a.Id == id && a.UserId == userId)
            .Select(a => a.BlobReferenceId)
            .SingleOrDefaultAsync(cancellationToken);
        if (referenceId.HasValue)
        {
            await using var source = await _blobStore.OpenReferenceReadAsync(referenceId.Value, userId, cancellationToken);
            if (source is null) return false;
            await source.CopyToAsync(destination, 64 * 1024, cancellationToken);
            return true;
        }
        await _context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = _context.Database.GetDbConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT b.rowid, b.Content
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

            // Microsoft.Data.Sqlite returns a true incremental SqliteBlob stream only
            // when the result set includes rowid. Without b.rowid above, GetStream
            // materializes the complete BLOB into a MemoryStream before this copy starts.
            await using var source = reader.GetStream(1);
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
        AuditLog? boardAuditLog,
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
            if (boardAuditLog is not null)
                await _context.AuditLogs.AddAsync(boardAuditLog, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            if (artefact.BlobReferenceId.HasValue)
                await _blobStore.ReleaseAsync(artefact.BlobReferenceId.Value, userId, cancellationToken);
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

    private static CaptureModality ToModality(ArtefactKind kind) => kind switch
    {
        ArtefactKind.Image => CaptureModality.Image,
        ArtefactKind.Pdf => CaptureModality.Document,
        ArtefactKind.TextFile => CaptureModality.Text,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
