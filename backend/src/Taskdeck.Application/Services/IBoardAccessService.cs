using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

/// <summary>
/// Service interface for managing board access permissions.
/// SCAFFOLDING: Implementation pending.
/// </summary>
public interface IBoardAccessService
{
    Task<Result<BoardAccessDto>> GrantAccessAsync(GrantAccessDto dto, Guid grantedBy);
    Task<Result<BoardAccessDto>> UpdateAccessAsync(Guid boardId, Guid accessId, UpdateAccessDto dto, Guid updatedBy);
    Task<Result> RevokeAccessAsync(Guid boardId, Guid accessId, Guid revokedBy);
    Task<Result<IEnumerable<BoardAccessDto>>> GetBoardAccessListAsync(Guid boardId);
    Task<Result<IEnumerable<BoardDto>>> GetUserBoardsAsync(Guid userId);
}
