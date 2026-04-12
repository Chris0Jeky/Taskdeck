namespace Taskdeck.Application.DTOs;

/// <summary>
/// Information about a configured OIDC provider, exposed to the frontend.
/// Secrets are never included.
/// </summary>
public record OidcProviderInfoDto(
    string Name,
    string DisplayName);
