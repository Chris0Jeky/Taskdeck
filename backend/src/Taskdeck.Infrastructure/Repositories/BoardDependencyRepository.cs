using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;
namespace Taskdeck.Infrastructure.Repositories;

public sealed class BoardDependencyRepository(TaskdeckDbContext context) : IBoardDependencyRepository
{
    public Task<BoardDependencies?> GetAsync(Guid boardId, CancellationToken cancellationToken) =>
        context.Set<BoardDependencies>().Include(graph => graph.Relations)
            .SingleOrDefaultAsync(graph => graph.BoardId == boardId, cancellationToken);

    public async Task<IReadOnlyList<UserDataExportCardRelationDto>> GetExportPageByUserIdAsync(
        Guid userId, int offset, int limit, CancellationToken cancellationToken)
    {
        // This must match CardRepository.GetExportPageByUserIdAsync: archived cards are portable,
        // but each relation endpoint has to be on a board the account owns or can read.
        var accessibleCards = context.Cards.Where(card =>
            card.Board.OwnerId == userId || card.Board.BoardAccesses.Any(access => access.UserId == userId));

        return await context.Set<CardRelation>()
            .AsNoTracking()
            .Where(relation =>
                accessibleCards.Any(card =>
                    card.Id == relation.SourceCardId && card.BoardId == relation.BoardId) &&
                accessibleCards.Any(card =>
                    card.Id == relation.TargetCardId && card.BoardId == relation.BoardId))
            .OrderBy(relation => relation.BoardId)
            .ThenBy(relation => relation.SourceCardId)
            .ThenBy(relation => relation.TargetCardId)
            .ThenBy(relation => relation.RelationType)
            .Skip(Math.Max(offset, 0))
            .Take(Math.Clamp(limit, 1, 500))
            .Select(relation => new UserDataExportCardRelationDto(
                relation.BoardId,
                relation.SourceCardId,
                relation.TargetCardId,
                relation.RelationType))
            .ToListAsync(cancellationToken);
    }

    public void AddForImport(BoardDependencies graph) => context.Set<BoardDependencies>().Add(graph);

    public async Task<bool> StageAsync(BoardDependencies graph, long expectedRevision, CancellationToken cancellationToken)
    {
        var entry = context.Entry(graph);
        if (entry.State == EntityState.Detached)
        {
            var stored = await GetAsync(graph.BoardId, cancellationToken);
            if (stored is null)
            {
                if (expectedRevision != 0) return false;
                context.Add(graph);
            }
            else
            {
                if (stored.Revision != expectedRevision) return false;
                stored.ReplaceRelations(graph.ReadRelations());
                entry = context.Entry(stored);
            }
        }
        if (entry.State != EntityState.Added)
        {
            entry.Property(value => value.Revision).OriginalValue = expectedRevision;
            entry.Property(value => value.Revision).IsModified = true;
        }
        return true;
    }

    public async Task<bool> SaveAsync(BoardDependencies graph, long expectedRevision, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (!await StageAsync(graph, expectedRevision, cancellationToken)) return false;
            await context.SaveChangesAsync(cancellationToken);
            // The write lock makes endpoint validation atomic with card deletion and audit.
            var ids = graph.ReadRelations().SelectMany(e => new[] { e.SourceCardId, e.TargetCardId }).Distinct().ToArray();
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
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteExtendedErrorCode: 1555 or 2067 or 787 })
        { context.ChangeTracker.Clear(); return false; }
    }
}
