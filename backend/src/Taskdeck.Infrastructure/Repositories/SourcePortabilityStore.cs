using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public sealed class SourcePortabilityStore(TaskdeckDbContext db) : ISourcePortabilityStore
{
    public async Task<long> EstimateBufferedBytesAsync(Guid userId, CancellationToken ct)
    {
        var bytes = await db.StoredBlobs.Where(x => x.OwnerUserId == userId).SumAsync(x => (long?)x.ByteSize, ct) ?? 0;
        var objects = await db.StoredBlobs.CountAsync(x => x.OwnerUserId == userId, ct);
        var references = await db.StoredBlobReferences.CountAsync(x => x.OwnerUserId == userId, ct);
        var representations = await db.Representations.CountAsync(x => x.UserId == userId, ct);
        var answers = await db.ThinkingAudioAnswers.CountAsync(x => x.UserId == userId, ct);
        // Conservative budget includes base64 + UTF-16 JSON buffers and bounded metadata per row.
        return checked(bytes * 4 + (long)(objects + references + representations + answers) * 4096);
    }
    public async IAsyncEnumerable<SourceBlobObjectExportDto> ObjectsAsync(Guid userId, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var row in db.StoredBlobs.AsNoTracking().Where(x => x.OwnerUserId == userId && x.ContentHash != null).OrderBy(x => x.Id).AsAsyncEnumerable().WithCancellation(ct))
            yield return new(row.Id, row.ContentHash!, row.ByteSize);
    }
    public async IAsyncEnumerable<SourceBlobReferenceExportDto> ReferencesAsync(Guid userId, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var row in db.StoredBlobReferences.AsNoTracking().Where(x => x.OwnerUserId == userId).OrderBy(x => x.Id).AsAsyncEnumerable().WithCancellation(ct))
            yield return new(row.Id, row.BlobId, row.Modality.ToString(), row.ReferrerKind, row.ReferrerId, row.AcquiredAt);
    }
    public async IAsyncEnumerable<SourceBlobChunkExportDto> ChunksAsync(Guid userId, [EnumeratorCancellation] CancellationToken ct)
    {
        var owned = db.StoredBlobs.Where(x => x.OwnerUserId == userId).Select(x => x.Id);
        await foreach (var row in db.StoredBlobChunks.AsNoTracking().Where(x => owned.Contains(x.BlobId)).OrderBy(x => x.BlobId).ThenBy(x => x.Ordinal).AsAsyncEnumerable().WithCancellation(ct))
            yield return new(row.BlobId, row.Ordinal, row.Content);
    }
    public async IAsyncEnumerable<RepresentationDescriptor> RepresentationsAsync(Guid userId, [EnumeratorCancellation] CancellationToken ct)
    {
        const int size = 100;
        for (var offset = 0; ; offset += size)
        {
            var rows = await db.Representations.AsNoTracking().Where(x => x.UserId == userId).OrderBy(x => x.Id).Skip(offset).Take(size).ToListAsync(ct);
            var ids = rows.Select(x => x.Id).ToArray();
            var edges = await db.RepresentationSupersessions.AsNoTracking().Where(x => ids.Contains(x.RepresentationId)).ToDictionaryAsync(x => x.RepresentationId, ct);
            foreach (var row in rows) yield return RepresentationDescriptor.FromRepresentation(row) with
            { SupersededByRepresentationId = edges.GetValueOrDefault(row.Id)?.SupersededByRepresentationId };
            if (rows.Count < size) yield break;
        }
    }
    public async IAsyncEnumerable<ThinkingAudioExportDto> AudioAnswersAsync(Guid userId, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var row in db.ThinkingAudioAnswers.AsNoTracking().Where(x => x.UserId == userId).OrderBy(x => x.Id).AsAsyncEnumerable().WithCancellation(ct))
            yield return new(row.Id, row.BoardId, row.CardId, row.LayerId, row.QuestionHash, row.CaptureId, row.SourceAssetId,
                row.UploadId, row.Revision, row.RepresentationId, row.ConfirmedMemoryId, row.CreatedAt, row.UpdatedAt);
    }
}
