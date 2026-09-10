using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public sealed class ThinkingAudioRepository(TaskdeckDbContext db) : IThinkingAudioRepository
{
    public async Task<IReadOnlyList<ThinkingAudioLibraryEntry>> LibraryEntriesAsync(Guid userId, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var pageIds = ids.Distinct().Take(20).ToArray();
        if (pageIds.Length == 0) return [];
        // One owner-scoped projection: no capture graphs, full evidence, representations or blobs.
        return await (
            from answer in db.ThinkingAudioAnswers.AsNoTracking()
            join capture in db.Captures.AsNoTracking() on answer.CaptureId equals capture.Id
            join asset in db.Set<SourceAsset>().AsNoTracking() on answer.SourceAssetId equals asset.Id
            from evidence in db.Set<SourceAsset>().AsNoTracking().Where(source => source.CaptureId == capture.Id && source.Ordinal == 1 && source.TextPayload != null)
            where answer.UserId == userId && capture.UserId == userId && pageIds.Contains(answer.Id) && asset.CaptureId == capture.Id
            orderby answer.Id
            select new ThinkingAudioLibraryEntry(answer.Id, asset.OriginalName ?? "original-audio", asset.ByteSize,
                answer.CreatedAt, evidence.TextPayload!.Text.Length > 500 ? evidence.TextPayload.Text.Substring(0, 500) + "…" : evidence.TextPayload.Text,
                answer.RepresentationId.HasValue, answer.ConfirmedMemoryId.HasValue, answer.BoardId == null)
        ).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ThinkingAudioAnswer>> ListByUserAsync(Guid userId, int offset, int limit, CancellationToken ct) =>
        await db.ThinkingAudioAnswers.AsNoTracking().Where(x => x.UserId == userId)
            .OrderBy(x => x.Id).Skip(offset).Take(limit).ToListAsync(ct);
    public Task<ThinkingAudioAnswer?> QuestionAsync(Guid userId, Guid cardId, Guid layerId, string hash, CancellationToken ct) =>
        db.Set<ThinkingAudioAnswer>().SingleOrDefaultAsync(x => x.UserId == userId && x.CardId == cardId && x.LayerId == layerId && x.QuestionHash == hash, ct);
    public Task<ThinkingAudioAnswer?> GetAsync(Guid userId, Guid id, CancellationToken ct) =>
        db.Set<ThinkingAudioAnswer>().SingleOrDefaultAsync(x => x.UserId == userId && x.Id == id, ct);
    public Task<ThinkingAudioAnswer?> UploadAsync(Guid userId, Guid uploadId, CancellationToken ct) =>
        db.Set<ThinkingAudioAnswer>().SingleOrDefaultAsync(x => x.UserId == userId && x.UploadId == uploadId, ct);
    public void Add(ThinkingAudioAnswer answer) => db.Set<ThinkingAudioAnswer>().Add(answer);
    public async Task<bool> SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); return true; }
        catch (DbUpdateConcurrencyException) { return false; }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 19 }) { return false; }
    }
}
