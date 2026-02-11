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

public record GrantAccessDto(
    Guid BoardId,
    Guid UserId,
    UserRole Role);

public record UpdateAccessDto(
    UserRole Role);
