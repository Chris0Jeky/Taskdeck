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

    /// <summary>
    /// Batched write-capability check: of <paramref name="boardIds"/>, which may
    /// <paramref name="userId"/> write to? The admitted set is exactly the one
    /// <c>BoardAccess.CanWrite()</c> admits (Owner / Admin / Editor) plus board ownership.
    /// <para>
    /// Batched on purpose: this is what lets a page of boards be annotated with a write
    /// signal without calling <see cref="CanWriteBoardAsync"/> once per board (an N+1 of a
    /// board fetch plus a membership read each).
    /// </para>
    /// </summary>
    Task<Result<IReadOnlySet<Guid>>> GetWritableBoardIdsAsync(
        Guid userId,
        IEnumerable<Guid> boardIds,
        CancellationToken cancellationToken = default);

    Task<Result<bool>> CanReadBoardAsync(Guid userId, Guid boardId);
    Task<Result<bool>> CanWriteBoardAsync(Guid userId, Guid boardId);
    Task<Result<bool>> CanManageBoardAccessAsync(Guid userId, Guid boardId);
    Task<Result<bool>> CanDeleteBoardAsync(Guid userId, Guid boardId);
    Task<Result<UserRole?>> GetUserRoleForBoardAsync(Guid userId, Guid boardId);
}
