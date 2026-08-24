using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

public interface IWorkspaceService
{
    Task<Result<WorkspaceHomeDto>> GetHomeAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Result<WorkspaceTodayDto>> GetTodayAsync(
        Guid userId,
        DateOnly? localDate = null,
        CancellationToken cancellationToken = default);
    /// <summary>
    /// Returns the authoritative collaboration-membership signal for the given user.
    /// This is the only supported input for deciding whether author-partitioned UI
    /// (an "All vs Mine" split) can mean anything for this user.
    /// </summary>
    Task<Result<WorkspaceCollaborationDto>> GetCollaborationAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
    Task<Result<WorkspacePreferenceDto>> GetPreferencesAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Result<WorkspacePreferenceDto>> UpdatePreferencesAsync(
        Guid userId,
        UpdateWorkspacePreferenceDto dto,
        CancellationToken cancellationToken = default);
    Task<Result<WorkspaceOnboardingDto>> UpdateOnboardingAsync(
        Guid userId,
        UpdateWorkspaceOnboardingDto dto,
        CancellationToken cancellationToken = default);
    Task<Result<WorkspaceCalendarDto>> GetCalendarAsync(
        Guid userId,
        DateTimeOffset from,
        DateTimeOffset to,
        DateOnly? localDate = null,
        CancellationToken cancellationToken = default);
}
