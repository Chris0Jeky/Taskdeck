using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface IThinkingDeckRepository
{
    Task<ThinkingDeck?> GetAsync(Guid cardId, CancellationToken cancellationToken);
    Task<bool> SaveAsync(ThinkingDeck deck, long expectedRevision, CancellationToken cancellationToken);
    Task<IReadOnlyList<ThinkingDeck>> GetByCardIdsAsync(IReadOnlyCollection<Guid> cardIds, CancellationToken cancellationToken);
    // Stage alongside the new card; the existing board-import unit of work commits both atomically.
    void AddForImport(ThinkingDeck deck);
}
