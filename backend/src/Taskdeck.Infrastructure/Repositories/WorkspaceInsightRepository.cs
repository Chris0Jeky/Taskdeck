using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public class WorkspaceInsightRepository(TaskdeckDbContext db) : IWorkspaceInsightRepository
{
    public Task<WorkspaceMemory?> ThinkingAnswerAsync(Guid userId, Guid cardId, Guid layerId, string questionHash, CancellationToken ct) =>
        db.Set<WorkspaceMemory>().Include(x => x.History).SingleOrDefaultAsync(x => x.UserId == userId && x.SourceCardId == cardId && x.SourceLayerId == layerId && x.SourceQuestionHash == questionHash, ct);
    public Task<List<QuietInsight>> InsightsAsync(Guid userId, Guid boardId, CancellationToken ct) => db.Set<QuietInsight>().Where(x => x.UserId == userId && x.BoardId == boardId).ToListAsync(ct);
    public Task<QuietInsight?> InsightAsync(Guid userId, Guid id, CancellationToken ct) => db.Set<QuietInsight>().SingleOrDefaultAsync(x => x.UserId == userId && x.Id == id, ct);
    public Task<List<WorkspaceMemory>> MemoriesAsync(Guid userId, Guid boardId, CancellationToken ct) => db.Set<WorkspaceMemory>().Include(x => x.History).Where(x => x.UserId == userId && x.BoardId == boardId).ToListAsync(ct);
    public Task<WorkspaceMemory?> MemoryAsync(Guid userId, Guid id, CancellationToken ct) => db.Set<WorkspaceMemory>().Include(x => x.History).SingleOrDefaultAsync(x => x.UserId == userId && x.Id == id, ct);
    public void Add(QuietInsight insight) => db.Set<QuietInsight>().Add(insight);
    public void Add(WorkspaceMemory memory) => db.Set<WorkspaceMemory>().Add(memory);
    public async Task<bool> SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); return true; }
        catch (DbUpdateConcurrencyException) { return false; }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 19 }) { return false; }
    }
}
