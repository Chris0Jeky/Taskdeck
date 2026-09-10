using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public sealed class WorkspaceAttentionRepository(TaskdeckDbContext db, IUserPreferenceRepository preferences) : IWorkspaceAttentionRepository
{
    public Task<UserPreference> GetAsync(Guid userId, CancellationToken ct) => preferences.GetOrCreateDefaultByUserIdAsync(userId, ct);

    public async Task<bool> SaveAsync(Guid userId, long expectedRevision, WorkspaceAttention state, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(state);
        // One conditional update is the budget boundary, including simultaneous tabs and devices.
        return await db.UserPreferences.Where(x => x.UserId == userId && x.AttentionRevision == expectedRevision)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.AttentionJson, json)
                .SetProperty(x => x.AttentionRevision, expectedRevision + 1), ct) == 1;
    }
}
