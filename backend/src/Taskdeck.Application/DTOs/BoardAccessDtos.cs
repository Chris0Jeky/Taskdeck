using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.DTOs;

// BoardAccess DTOs
public record BoardAccessDto(
    Guid Id,
    Guid BoardId,
    Guid UserId,
    UserRole Role,
    Guid GrantedBy,
    DateTimeOffset GrantedAt);

// Grant board access to a user. Provide either the raw <see cref="UserId"/> (compatibility path)
// or an <see cref="Identifier"/> (email or username) that is resolved to a user id inside
// BoardAccessService, behind the CanManageBoardAccess gate. When Identifier is supplied it takes
// precedence over UserId.
public record GrantAccessDto(
    Guid BoardId,
    Guid UserId,
    UserRole Role,
    string? Identifier = null);

public record UpdateAccessDto(
    UserRole Role);
