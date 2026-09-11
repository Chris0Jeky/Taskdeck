using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public sealed class CardAssignmentStore(TaskdeckDbContext context) : ICardAssignmentStore
{
    public async Task RefreshAuthorityAsync(Guid boardId, Guid actorId, CancellationToken ct)
    {
        foreach (var entry in context.ChangeTracker.Entries().Where(e =>
                     e.State == EntityState.Unchanged && (e.Entity is Board b && b.Id == boardId ||
                     e.Entity is BoardAccess a && a.BoardId == boardId ||
                     e.Entity is User u && u.Id == actorId)).ToList())
            await entry.ReloadAsync(ct);
    }

    public async Task<Card?> ReadCardAsync(Guid boardId, Guid cardId, CancellationToken ct)
    {
        DiscardCardSnapshots(cardId);
        return await context.Cards.Include(c => c.Assignments).ThenInclude(a => a.User)
            .Include(c => c.CardLabels).ThenInclude(l => l.Label)
            .SingleOrDefaultAsync(c => c.Id == cardId && c.BoardId == boardId, ct);
    }

    public async Task<IReadOnlyList<User>> ReadParticipantsAsync(Guid boardId, CancellationToken ct)
        => await context.Users.AsNoTracking().Where(u => u.IsActive &&
            (context.Boards.Any(b => b.Id == boardId && b.OwnerId == u.Id) ||
             context.BoardAccesses.Any(a => a.BoardId == boardId && a.UserId == u.Id)))
            .OrderBy(u => u.Username).ToListAsync(ct);

    public async Task<IReadOnlyList<Card>> ReadAssignedCardsAsync(Guid userId, Guid? boardId, CancellationToken ct)
    {
        DiscardCardSnapshots(null);
        return await context.Cards.Include(c => c.Assignments).ThenInclude(a => a.User)
            .Where(c => (!boardId.HasValue || c.BoardId == boardId) && c.Assignments.Any(a => a.UserId == userId))
            .ToListAsync(ct);
    }

    private void DiscardCardSnapshots(Guid? cardId)
    {
        foreach (var entry in context.ChangeTracker.Entries().Where(e => e.State == EntityState.Unchanged &&
                     (e.Entity is Card c && (!cardId.HasValue || c.Id == cardId) ||
                      e.Entity is CardAssignment a && (!cardId.HasValue || a.CardId == cardId))).ToList())
            entry.State = EntityState.Detached;
    }
}
