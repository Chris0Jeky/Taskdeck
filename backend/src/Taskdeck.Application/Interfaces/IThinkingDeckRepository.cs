using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface IThinkingDeckRepository
{
    Task<ThinkingDeck?> GetAsync(Guid cardId, CancellationToken cancellationToken);
    Task<bool> SaveAsync(ThinkingDeck deck, long expectedRevision, CancellationToken cancellationToken);
}
