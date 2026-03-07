using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

public interface IWorkspaceService
{
    Task<Result<WorkspaceHomeDto>> GetHomeAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Result<WorkspacePreferenceDto>> GetPreferencesAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Result<WorkspacePreferenceDto>> UpdatePreferencesAsync(
        Guid userId,
        UpdateWorkspacePreferenceDto dto,
        CancellationToken cancellationToken = default);
}
