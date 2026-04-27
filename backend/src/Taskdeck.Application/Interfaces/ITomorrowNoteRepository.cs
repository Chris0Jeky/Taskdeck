using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface ITomorrowNoteRepository : IRepository<TomorrowNote>
{
    Task<TomorrowNote?> GetByUserAndDateAsync(Guid userId, DateOnly date, CancellationToken cancellationToken = default);
}
