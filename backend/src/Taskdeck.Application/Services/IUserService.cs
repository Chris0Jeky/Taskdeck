using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

/// <summary>
/// Service interface for user management operations.
/// SCAFFOLDING: Implementation pending.
/// </summary>
public interface IUserService
{
    Task<Result<UserDto>> CreateUserAsync(CreateUserDto dto);
    Task<Result<UserDto>> GetUserByIdAsync(Guid userId);
    Task<Result<UserDto>> GetUserByUsernameAsync(string username);
    Task<Result<UserDto>> GetUserByEmailAsync(string email);
    Task<Result<UserDto>> UpdateUserAsync(Guid userId, UpdateUserDto dto);
    Task<Result> DeactivateUserAsync(Guid userId);
    Task<Result> ActivateUserAsync(Guid userId);
    Task<Result<IEnumerable<UserDto>>> ListUsersAsync();
}
