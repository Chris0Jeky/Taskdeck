using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public class CardCommentRepository : Repository<CardComment>, ICardCommentRepository
{
    public CardCommentRepository(TaskdeckDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<CardComment>> GetByCardIdAsync(
        Guid cardId,
        CancellationToken cancellationToken = default)
    {
        var comments = await _dbSet
            .Where(comment => comment.CardId == cardId)
            .Include(comment => comment.AuthorUser)
            .Include(comment => comment.Mentions)
                .ThenInclude(mention => mention.MentionedUser)
            .ToListAsync(cancellationToken);

        return comments
            .OrderBy(comment => comment.CreatedAt)
            .ToList();
    }

    public async Task<CardComment?> GetByIdWithMentionsAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(comment => comment.AuthorUser)
            .Include(comment => comment.Mentions)
                .ThenInclude(mention => mention.MentionedUser)
            .FirstOrDefaultAsync(comment => comment.Id == id, cancellationToken);
    }
}
