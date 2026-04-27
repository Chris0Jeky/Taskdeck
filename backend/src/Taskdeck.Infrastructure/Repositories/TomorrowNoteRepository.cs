using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public class TomorrowNoteRepository : Repository<TomorrowNote>, ITomorrowNoteRepository
{
    public TomorrowNoteRepository(TaskdeckDbContext context) : base(context)
    {
    }

    public async Task<TomorrowNote?> GetByUserAndDateAsync(
        Guid userId,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        return await _context.Set<TomorrowNote>()
            .FirstOrDefaultAsync(
                note => note.UserId == userId && note.Date == date,
                cancellationToken);
    }
}
