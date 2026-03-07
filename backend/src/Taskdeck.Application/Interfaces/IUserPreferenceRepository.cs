using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface IUserPreferenceRepository : IRepository<UserPreference>
{
    Task<UserPreference?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
}
