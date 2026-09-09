using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public sealed class ThinkingAudioRepository(TaskdeckDbContext db) : IThinkingAudioRepository
{
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
