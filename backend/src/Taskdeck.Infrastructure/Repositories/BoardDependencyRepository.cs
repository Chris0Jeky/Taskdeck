using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;
namespace Taskdeck.Infrastructure.Repositories;

public sealed class BoardDependencyRepository(TaskdeckDbContext context) : IBoardDependencyRepository
{
    public Task<BoardDependencies?> GetAsync(Guid boardId, CancellationToken cancellationToken) =>
        context.Set<BoardDependencies>().AsNoTracking().SingleOrDefaultAsync(graph => graph.BoardId == boardId, cancellationToken);
    public void AddForImport(BoardDependencies graph) => context.Set<BoardDependencies>().Add(graph);
    public async Task<bool> SaveAsync(BoardDependencies graph, long expectedRevision, CancellationToken cancellationToken)
    {
        var entry = context.Entry(graph);
        if (expectedRevision == 0) entry.State = EntityState.Added;
        else
        {
            entry.State = EntityState.Modified;
            entry.Property(value => value.Revision).OriginalValue = expectedRevision;
        }
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            // The write transaction serializes card deletion. Validate after acquiring its
            // write lock and before committing; an earlier deletion rolls back graph + audit.
            var ids = graph.ReadEdges().SelectMany(e => new[] { e.CardId, e.DependsOnCardId }).Distinct().ToArray();
            foreach (var batch in ids.Chunk(500))
                if (await context.Cards.CountAsync(card => card.BoardId == graph.BoardId && batch.Contains(card.Id), cancellationToken) != batch.Length)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    context.ChangeTracker.Clear();
                    return false;
                }
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException) { context.ChangeTracker.Clear(); return false; }
        catch (DbUpdateException ex) when (expectedRevision == 0 && ex.InnerException is SqliteException { SqliteExtendedErrorCode: 1555 or 2067 })
        { context.ChangeTracker.Clear(); return false; }
    }
}
