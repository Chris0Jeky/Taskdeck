using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface IUserRepository : IRepository<User>
{
    Task<IReadOnlyDictionary<Guid, string>> GetUsernamesByIdsAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default);
    Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string username, string email, CancellationToken cancellationToken = default);
}
