using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface IWorkspacePlanRepository
{
    Task<UserPreference> GetAsync(Guid userId, CancellationToken ct);
    Task<bool> SaveAsync(UserPreference preference, long expectedRevision, CancellationToken ct);
}
