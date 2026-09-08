using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public sealed class ThinkingDeckRepository(TaskdeckDbContext context) : IThinkingDeckRepository
{
    public async Task<IReadOnlyList<ThinkingDeck>> GetByCardIdsAsync(IReadOnlyCollection<Guid> cardIds, CancellationToken cancellationToken)
    {
        var result = new List<ThinkingDeck>();
        foreach (var batch in cardIds.Distinct().Chunk(500))
            result.AddRange(await context.Set<ThinkingDeck>().AsNoTracking().Where(deck => batch.Contains(deck.CardId)).ToListAsync(cancellationToken));
        return result;
    }

    public void AddForImport(ThinkingDeck deck) => context.Set<ThinkingDeck>().Add(deck);

    public Task<ThinkingDeck?> GetAsync(Guid cardId, CancellationToken cancellationToken) =>
        context.Set<ThinkingDeck>().AsNoTracking().SingleOrDefaultAsync(deck => deck.CardId == cardId, cancellationToken);

    public async Task<bool> SaveAsync(ThinkingDeck deck, long expectedRevision, CancellationToken cancellationToken)
    {
        var entry = context.Entry(deck);
        if (expectedRevision == 0) entry.State = EntityState.Added;
        else
        {
            entry.State = EntityState.Modified;
            entry.Property(value => value.Revision).OriginalValue = expectedRevision;
        }
        try { await context.SaveChangesAsync(cancellationToken); return true; }
        catch (DbUpdateConcurrencyException) { entry.State = EntityState.Detached; return false; }
        catch (DbUpdateException ex) when (expectedRevision == 0 && ex.InnerException is SqliteException { SqliteExtendedErrorCode: 1555 or 2067 })
        { entry.State = EntityState.Detached; return false; }
    }
}
