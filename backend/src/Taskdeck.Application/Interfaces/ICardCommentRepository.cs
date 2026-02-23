using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface ICardCommentRepository : IRepository<CardComment>
{
    Task<IEnumerable<CardComment>> GetByCardIdAsync(Guid cardId, CancellationToken cancellationToken = default);
    Task<CardComment?> GetByIdWithMentionsAsync(Guid id, CancellationToken cancellationToken = default);
}
