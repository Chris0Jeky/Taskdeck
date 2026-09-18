using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public sealed class SourcePortabilityStore(TaskdeckDbContext db) : ISourcePortabilityStore
{
    public async Task<IAsyncDisposable> OpenReadSnapshotAsync(CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Source export must open its own read snapshot before reading storage sections.");
        await db.Database.OpenConnectionAsync(ct);
        SqliteTransaction? transaction = null;
        try
        {
            // Deferred reads do not reserve the writer. WAL writers may commit while
            // a streamed export retains the snapshot established by its first query.
            transaction = ((SqliteConnection)db.Database.GetDbConnection()).BeginTransaction(deferred: true);
            var enlisted = (await db.Database.UseTransactionAsync(transaction, ct))!;
            return new ReadSnapshot(db, transaction, enlisted);
        }
        catch
        {
            if (transaction is not null) await transaction.DisposeAsync();
            await db.Database.CloseConnectionAsync();
            throw;
        }
    }

    private sealed class ReadSnapshot(TaskdeckDbContext context, SqliteTransaction transaction, IDbContextTransaction enlisted) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try { await enlisted.DisposeAsync(); }
            finally
            {
                try { await transaction.DisposeAsync(); }
                finally { await context.Database.CloseConnectionAsync(); }
            }
        }
    }

    public async Task<long> EstimateBufferedBytesAsync(Guid userId, CancellationToken ct)
    {
        var bytes = await db.StoredBlobs.Where(x => x.OwnerUserId == userId).SumAsync(x => (long?)x.ByteSize, ct) ?? 0;
        var objects = await db.StoredBlobs.CountAsync(x => x.OwnerUserId == userId, ct);
        var references = await db.StoredBlobReferences.CountAsync(x => x.OwnerUserId == userId, ct);
        var representations = await db.Representations.CountAsync(x => x.UserId == userId, ct);
        var answers = await db.ThinkingAudioAnswers.CountAsync(x => x.UserId == userId, ct);
        var attempts = await db.AudioTranscriptionAttempts.CountAsync(x => x.UserId == userId, ct);
        var budgets = await db.AudioTranscriptionBudgets.CountAsync(x => x.UserId == userId, ct);
        // Conservative budget includes base64 + UTF-16 JSON buffers and bounded metadata per row.
        return checked(bytes * 4 + (long)(objects + references + representations + answers + attempts + budgets) * 4096);
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
                row.UploadId, row.Revision, row.RepresentationId, row.ConfirmedMemoryId, row.CreatedAt, row.UpdatedAt, row.ConfirmationRequestHash);
    }

    public async IAsyncEnumerable<AudioTranscriptionAttemptExportDto> AudioTranscriptionAttemptsAsync(Guid userId, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var row in db.AudioTranscriptionAttempts.AsNoTracking().Where(x => x.UserId == userId).OrderBy(x => x.Id).AsAsyncEnumerable().WithCancellation(ct))
            yield return new(row.Id, row.UserId, row.AudioAnswerId, row.CaptureId, row.SourceAssetId, row.RequestId, row.RequestHash,
                row.ConfigurationHash, row.Provider, row.Model, row.StartedAt, row.Deadline, row.FinishedAt, row.State.ToString(), row.FailureCode, row.RepresentationId, row.Revision);
    }
    public async IAsyncEnumerable<AudioTranscriptionBudgetExportDto> AudioTranscriptionBudgetsAsync(Guid userId, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var row in db.AudioTranscriptionBudgets.AsNoTracking().Where(x => x.UserId == userId).AsAsyncEnumerable().WithCancellation(ct))
            yield return new(row.UserId, row.UtcDay, row.Attempts, row.InputBytes, row.Revision);
    }
}
