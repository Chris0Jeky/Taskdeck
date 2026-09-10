using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public class WorkspaceInsightRepository(TaskdeckDbContext db) : IWorkspaceInsightRepository
{
    public async Task<IReadOnlyList<WorkspaceMemory>> MemoriesByUserAsync(Guid userId, int limit, int offset, CancellationToken ct) =>
        await db.Set<WorkspaceMemory>().AsNoTracking().Include(x => x.History).Where(x => x.UserId == userId)
            .OrderBy(x => x.Id).Skip(offset).Take(limit).ToListAsync(ct);
    public async Task<IReadOnlyList<QuietInsight>> InsightsByUserAsync(Guid userId, int limit, int offset, CancellationToken ct) =>
        await db.Set<QuietInsight>().AsNoTracking().Where(x => x.UserId == userId)
            .OrderBy(x => x.Id).Skip(offset).Take(limit).ToListAsync(ct);
    public async Task<(int Memories, int Revisions, int Insights)> DeleteByUserAsync(Guid userId, CancellationToken ct)
    {
        // The account workflow anonymizes User in place. Its FK cascade cannot erase these
        // private records, including records attached to surviving collaborators' boards.
        var memoryIds = db.Set<WorkspaceMemory>().Where(x => x.UserId == userId).Select(x => x.Id);
        var revisions = await db.Set<WorkspaceMemoryRevision>().Where(x => memoryIds.Contains(x.MemoryId)).ExecuteDeleteAsync(ct);
        var memories = await db.Set<WorkspaceMemory>().Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        var insights = await db.Set<QuietInsight>().Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        return (memories, revisions, insights);
    }
    public Task<WorkspaceMemory?> ThinkingAnswerAsync(Guid userId, Guid cardId, Guid layerId, string questionHash, CancellationToken ct) =>
        db.Set<WorkspaceMemory>().Include(x => x.History).SingleOrDefaultAsync(x => x.UserId == userId && x.SourceCardId == cardId && x.SourceLayerId == layerId && x.SourceQuestionHash == questionHash, ct);
    public Task<List<QuietInsight>> InsightsAsync(Guid userId, Guid boardId, CancellationToken ct) => db.Set<QuietInsight>().Where(x => x.UserId == userId && x.BoardId == boardId).ToListAsync(ct);
    public Task<QuietInsight?> InsightAsync(Guid userId, Guid id, CancellationToken ct) => db.Set<QuietInsight>().SingleOrDefaultAsync(x => x.UserId == userId && x.Id == id, ct);
    public Task<List<WorkspaceMemory>> MemoriesAsync(Guid userId, Guid boardId, CancellationToken ct) => db.Set<WorkspaceMemory>().Include(x => x.History).Where(x => x.UserId == userId && x.BoardId == boardId).ToListAsync(ct);
    public Task<WorkspaceMemory?> MemoryAsync(Guid userId, Guid id, CancellationToken ct) => db.Set<WorkspaceMemory>().Include(x => x.History).SingleOrDefaultAsync(x => x.UserId == userId && x.Id == id, ct);
    public void Add(QuietInsight insight) => db.Set<QuietInsight>().Add(insight);
    public void Add(WorkspaceMemory memory) => db.Set<WorkspaceMemory>().Add(memory);
    public void GuardMemoryRevision(WorkspaceMemory memory) => db.Entry(memory).Property(x => x.Revision).IsModified = true;
    public async Task<bool> SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); return true; }
        catch (DbUpdateConcurrencyException) { return false; }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 19 }) { return false; }
    }

    public async Task<bool> SaveObservationAsync(Guid userId, Guid boardId, Guid cardId, string fingerprint, CancellationToken ct)
    {
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
        var committed = false;
        try
        {
            // SQLite begins an immediate write transaction. The short source/access check and
            // save therefore cannot straddle a concurrent card edit or membership revocation.
            transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var source = await new WorkspaceObservationReader(db).SourceAsync(userId, boardId, cardId, ct);
            if (source == null || source.Fingerprint != fingerprint) return false;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            committed = true;
            return true;
        }
        catch (DbUpdateConcurrencyException) { return false; }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 5 or 6 or 19 }) { return false; }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode is 5 or 6) { return false; }
        finally
        {
            if (!committed)
            {
                try { if (transaction != null) await transaction.RollbackAsync(CancellationToken.None); }
                finally
                {
                    // A later save in this request must never flush rejected observation changes.
                    foreach (var entry in db.ChangeTracker.Entries<QuietInsight>()
                        .Where(x => x.Entity.UserId == userId && x.Entity.BoardId == boardId).ToList())
                        entry.State = EntityState.Detached;
                }
            }
            if (transaction != null) await transaction.DisposeAsync();
        }
    }
}
