using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Storage;

/// <summary>
/// Mutations require the caller's SQLite write transaction. Quota is checked under that lock
/// before reading. A savepoint rolls back a failed upload even if its caller catches the error.
/// SQL writes only byte-store rows, never flushes unrelated tracked application work.
/// </summary>
public sealed class SqliteBlobStore(TaskdeckDbContext db, BlobStorageSettings settings) : IBlobStore
{
    private void RequireTransaction()
    {
        if (!db.Database.IsSqlite() || db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Blob writes require the caller's SQLite transaction.");
    }

    private async Task CheckQuota(Guid owner, CaptureModality modality, long size, CancellationToken ct)
    {
        if (owner == Guid.Empty || !Enum.IsDefined(modality) || size < 0)
            throw new DomainException(ErrorCodes.ValidationError, "Invalid byte-store owner, modality or size.");
        if (settings.MaximumUploadBytes <= 0 || settings.OwnerQuotaBytes <= 0 || settings.ModalityQuotaBytes <= 0 || settings.MaximumReferencesPerOwner <= 0)
            throw new InvalidOperationException("Byte-store quotas must be positive.");
        var total = await db.StoredBlobs.Where(x => x.OwnerUserId == owner).SumAsync(x => x.ByteSize, ct);
        var modalityIds = db.StoredBlobReferences.Where(x => x.OwnerUserId == owner && x.Modality == modality).Select(x => x.BlobId);
        var modalityTotal = await db.StoredBlobs.Where(x => x.OwnerUserId == owner && modalityIds.Contains(x.Id)).SumAsync(x => x.ByteSize, ct);
        var references = await db.StoredBlobReferences.CountAsync(x => x.OwnerUserId == owner, ct);
        if (size > settings.MaximumUploadBytes || total > settings.OwnerQuotaBytes - size || modalityTotal > settings.ModalityQuotaBytes - size
            || references >= settings.MaximumReferencesPerOwner)
            throw new DomainException(ErrorCodes.PayloadTooLarge, "The audio/source storage quota would be exceeded.");
    }

    public async Task<BlobReference> AcquireAsync(BlobAcquisition acquisition, Stream content, CancellationToken cancellationToken = default)
    {
        RequireTransaction();
        if (acquisition.ExpectedByteSize <= 0 || acquisition.ReferrerKind?.Length > 100 || acquisition.ReferrerId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Provide a positive declared size and valid source reference.");
        // Take the SQLite writer lock even if the caller began a deferred transaction.
        await db.Database.ExecuteSqlRawAsync("UPDATE StoredBlobs SET ByteSize = ByteSize WHERE 0", cancellationToken);
        await CheckQuota(acquisition.OwnerUserId, acquisition.AssetModality, acquisition.ExpectedByteSize, cancellationToken);
        var transaction = db.Database.CurrentTransaction!;
        var savepoint = "blob_" + Guid.NewGuid().ToString("N");
        await transaction.CreateSavepointAsync(savepoint, cancellationToken);
        try
        {
            var blob = new StoredBlob(acquisition.OwnerUserId, acquisition.ExpectedByteSize);
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO StoredBlobs (Id, OwnerUserId, ContentHash, ByteSize) VALUES ({blob.Id}, {blob.OwnerUserId}, NULL, {blob.ByteSize})", cancellationToken);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[StoredBlobChunk.MaximumSize];
            long received = 0;
            var ordinal = 0;
            while (true)
            {
                var count = await content.ReadAsync(buffer.AsMemory(), cancellationToken);
                if (count == 0) break;
                if (count > acquisition.ExpectedByteSize - received)
                    throw new DomainException(ErrorCodes.PayloadTooLarge, "The upload exceeded its declared size.");
                received += count;
                hash.AppendData(buffer, 0, count);
                var chunk = buffer.AsSpan(0, count).ToArray();
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO StoredBlobChunks (BlobId, Ordinal, Content) VALUES ({blob.Id}, {ordinal}, {chunk})", cancellationToken);
                ordinal++;
            }
            if (received != acquisition.ExpectedByteSize)
                throw new DomainException(ErrorCodes.ValidationError, "The upload ended before its declared size. Retry with the original file.");
            var digest = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            var existing = await db.StoredBlobs.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerUserId == blob.OwnerUserId && x.ContentHash == digest, cancellationToken);
            if (existing is not null)
            {
                if (existing.ByteSize != received)
                    throw new DomainException(ErrorCodes.Conflict, "Stored source identity does not match its size.");
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM StoredBlobs WHERE Id = {blob.Id}", cancellationToken);
                blob = existing;
            }
            else
            {
                blob.Complete(digest);
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE StoredBlobs SET ContentHash = {digest} WHERE Id = {blob.Id}", cancellationToken);
            }
            var result = await AddReference(blob, acquisition.AssetModality, acquisition.ReferrerKind, acquisition.ReferrerId, cancellationToken);
            await transaction.ReleaseSavepointAsync(savepoint, cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackToSavepointAsync(savepoint, CancellationToken.None);
            await transaction.ReleaseSavepointAsync(savepoint, CancellationToken.None);
            throw;
        }
    }

    private async Task<BlobReference> AddReference(StoredBlob blob, CaptureModality modality, string? kind, Guid? id, CancellationToken ct)
    {
        var reference = new StoredBlobReference(blob, modality, kind, id);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO StoredBlobReferences (Id, BlobId, OwnerUserId, Modality, ReferrerKind, ReferrerId, AcquiredAt) VALUES ({reference.Id}, {blob.Id}, {blob.OwnerUserId}, {(int)modality}, {kind}, {id}, {reference.AcquiredAt})", ct);
        return new BlobReference(reference.Id, blob.Id, blob.OwnerUserId, blob.ContentHash!, blob.ByteSize, modality, reference.AcquiredAt);
    }

    public async Task<BlobReference?> AcquireExistingAsync(Guid ownerUserId, string contentHash, CaptureModality assetModality, string? referrerKind, Guid? referrerId, CancellationToken cancellationToken = default)
    {
        RequireTransaction();
        await db.Database.ExecuteSqlRawAsync("UPDATE StoredBlobs SET ByteSize = ByteSize WHERE 0", cancellationToken);
        var blob = await db.StoredBlobs.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerUserId == ownerUserId && x.ContentHash == contentHash, cancellationToken);
        if (blob is null) return null;
        await CheckQuota(ownerUserId, assetModality, 0, cancellationToken);
        var alreadyCounted = await db.StoredBlobReferences.AnyAsync(x => x.BlobId == blob.Id && x.Modality == assetModality, cancellationToken);
        if (!alreadyCounted)
        {
            var ids = db.StoredBlobReferences.Where(x => x.OwnerUserId == ownerUserId && x.Modality == assetModality).Select(x => x.BlobId);
            var size = await db.StoredBlobs.Where(x => x.OwnerUserId == ownerUserId && ids.Contains(x.Id)).SumAsync(x => x.ByteSize, cancellationToken);
            if (size > settings.ModalityQuotaBytes - blob.ByteSize)
                throw new DomainException(ErrorCodes.PayloadTooLarge, "The source modality storage quota would be exceeded.");
        }
        return await AddReference(blob, assetModality, referrerKind, referrerId, cancellationToken);
    }

    public async Task<bool> ReleaseAsync(Guid referenceId, Guid ownerUserId, CancellationToken cancellationToken = default)
    {
        RequireTransaction();
        await db.Database.ExecuteSqlRawAsync("UPDATE StoredBlobs SET ByteSize = ByteSize WHERE 0", cancellationToken);
        var reference = await db.StoredBlobReferences.AsNoTracking().SingleOrDefaultAsync(x => x.Id == referenceId && x.OwnerUserId == ownerUserId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "This source reference is unavailable.");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM StoredBlobReferences WHERE Id = {referenceId} AND OwnerUserId = {ownerUserId}", cancellationToken);
        if (await db.StoredBlobReferences.AnyAsync(x => x.BlobId == reference.BlobId, cancellationToken)) return false;
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM StoredBlobs WHERE Id = {reference.BlobId} AND OwnerUserId = {ownerUserId}", cancellationToken);
        return true;
    }

    public async Task<Stream?> OpenReadAsync(Guid blobObjectId, Guid ownerUserId, CancellationToken cancellationToken = default)
    {
        var blob = await db.StoredBlobs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == blobObjectId && x.OwnerUserId == ownerUserId && x.ContentHash != null, cancellationToken);
        return blob is null ? null : new ChunkStream(db, blob.Id, blob.OwnerUserId, blob.ByteSize);
    }

    public async Task<Stream?> OpenReferenceReadAsync(Guid referenceId, Guid ownerUserId, CancellationToken cancellationToken = default)
    {
        var id = await db.StoredBlobReferences.Where(x => x.Id == referenceId && x.OwnerUserId == ownerUserId).Select(x => (Guid?)x.BlobId).SingleOrDefaultAsync(cancellationToken);
        return id is null ? null : await OpenReadAsync(id.Value, ownerUserId, cancellationToken);
    }

    public async Task<BlobObjectDescriptor?> FindByHashAsync(Guid ownerUserId, string contentHash, CancellationToken cancellationToken = default)
    {
        var blob = await db.StoredBlobs.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerUserId == ownerUserId && x.ContentHash == contentHash, cancellationToken);
        return blob is null ? null : new BlobObjectDescriptor(blob.Id, blob.OwnerUserId, blob.ContentHash!, blob.ByteSize,
            await db.StoredBlobReferences.CountAsync(x => x.BlobId == blob.Id, cancellationToken));
    }

    public async Task<BlobQuotaUsage> GetUsageAsync(Guid ownerUserId, CancellationToken cancellationToken = default)
    {
        var byModality = new Dictionary<CaptureModality, long>();
        foreach (var modality in Enum.GetValues<CaptureModality>())
        {
            var ids = db.StoredBlobReferences.Where(x => x.OwnerUserId == ownerUserId && x.Modality == modality).Select(x => x.BlobId);
            byModality[modality] = await db.StoredBlobs.Where(x => x.OwnerUserId == ownerUserId && ids.Contains(x.Id)).SumAsync(x => x.ByteSize, cancellationToken);
        }
        return new BlobQuotaUsage(ownerUserId, await db.StoredBlobs.Where(x => x.OwnerUserId == ownerUserId).SumAsync(x => x.ByteSize, cancellationToken), byModality,
            await db.StoredBlobs.CountAsync(x => x.OwnerUserId == ownerUserId, cancellationToken),
            await db.StoredBlobReferences.CountAsync(x => x.OwnerUserId == ownerUserId, cancellationToken));
    }

    public Task<int> DeleteOwnerAsync(Guid ownerUserId, CancellationToken cancellationToken = default)
    {
        RequireTransaction();
        if (ownerUserId == Guid.Empty) throw new DomainException(ErrorCodes.ValidationError, "An owner is required.");
        return db.StoredBlobs.Where(x => x.OwnerUserId == ownerUserId).ExecuteDeleteAsync(cancellationToken);
    }

    private sealed class ChunkStream(TaskdeckDbContext context, Guid id, Guid owner, long length) : Stream
    {
        private byte[] buffer = [];
        private int offset;
        private int ordinal;
        private long position;
        private bool disposed;
        public override bool CanRead => !disposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => position; set => throw new NotSupportedException(); }
        public override int Read(byte[] bytes, int start, int count) => ReadAsync(bytes.AsMemory(start, count)).AsTask().GetAwaiter().GetResult();
        public override async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (destination.IsEmpty || position == length) return 0;
            if (offset == buffer.Length)
            {
                buffer = await context.StoredBlobChunks.Where(x => x.BlobId == id && x.Ordinal == ordinal
                    && context.StoredBlobs.Any(blob => blob.Id == id && blob.OwnerUserId == owner))
                    .Select(x => x.Content).SingleOrDefaultAsync(cancellationToken)
                    ?? throw new IOException("The stored source is no longer available.");
                offset = 0; ordinal++;
            }
            var count = Math.Min(destination.Length, buffer.Length - offset);
            buffer.AsMemory(offset, count).CopyTo(destination);
            offset += count; position += count;
            return count;
        }
        protected override void Dispose(bool disposing) { disposed = true; buffer = []; base.Dispose(disposing); }
        public override void Flush() { }
        public override long Seek(long value, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] bytes, int start, int count) => throw new NotSupportedException();
    }
}
