using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.Services;

/// <summary>
/// Service interface for board authorization and permission checks.
/// </summary>
public interface IAuthorizationService
{
    Task<Result<IReadOnlySet<Guid>>> GetReadableBoardIdsAsync(
        Guid userId,
        IEnumerable<Guid> boardIds,
        CancellationToken cancellationToken = default);

    Task<Result<bool>> CanReadBoardAsync(Guid userId, Guid boardId);
    Task<Result<bool>> CanWriteBoardAsync(Guid userId, Guid boardId);
    Task<Result<bool>> CanManageBoardAccessAsync(Guid userId, Guid boardId);
    Task<Result<bool>> CanDeleteBoardAsync(Guid userId, Guid boardId);
    Task<Result<UserRole?>> GetUserRoleForBoardAsync(Guid userId, Guid boardId);
}
