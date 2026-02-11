using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.DTOs;

// User DTOs
public record UserDto(
    Guid Id,
    string Username,
    string Email,
    UserRole DefaultRole,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record CreateUserDto(
    string Username,
    string Email,
    string Password,
    UserRole DefaultRole = UserRole.Editor);

public record UpdateUserDto(
    string? Username = null,
    string? Email = null);

public record LoginDto(
    string UsernameOrEmail,
    string Password);

public record AuthResultDto(
    string Token,
    UserDto User);
