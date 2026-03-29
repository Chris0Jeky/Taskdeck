using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

/// <summary>
/// Service interface for authentication operations.
/// SCAFFOLDING: Implementation pending. Will use JWT tokens.
/// </summary>
public interface IAuthenticationService
{
    Task<Result<AuthResultDto>> LoginAsync(LoginDto dto);
    Task<Result<AuthResultDto>> RegisterAsync(CreateUserDto dto);
    Task<Result> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword);
    Task<Result<UserDto>> ValidateTokenAsync(string token);
    Task<Result<AuthResultDto>> ExternalLoginAsync(ExternalLoginDto dto);
}
