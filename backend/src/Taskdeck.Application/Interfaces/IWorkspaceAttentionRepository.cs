using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface IWorkspaceAttentionRepository
{
    Task<UserPreference> GetAsync(Guid userId, CancellationToken ct);
    Task<bool> SaveAsync(Guid userId, long expectedRevision, WorkspaceAttention state, CancellationToken ct);
}
