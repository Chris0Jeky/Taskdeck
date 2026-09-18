using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface ICardAssignmentStore
{
    // Called after acquiring the caller's database write transaction; discard preflight
    // tracked snapshots so authorization and eligibility use the serialized database state.
    Task RefreshAuthorityAsync(Guid boardId, Guid actorId, CancellationToken cancellationToken);
    Task<Card?> ReadCardAsync(Guid boardId, Guid cardId, CancellationToken cancellationToken);
    Task<IReadOnlyList<User>> ReadParticipantsAsync(Guid boardId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Card>> ReadAssignedCardsAsync(Guid userId, Guid? boardId, CancellationToken cancellationToken);
}
