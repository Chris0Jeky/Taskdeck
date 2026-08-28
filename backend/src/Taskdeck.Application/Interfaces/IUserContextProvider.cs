using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.Interfaces;

/// <summary>Validated identity and capabilities for the current MCP caller.</summary>
public readonly record struct McpUserContext(Guid UserId, ApiKeyScope Scopes);

/// <summary>
/// Provides the current user's identity for MCP server operations.
/// Abstracts over stdio (local OS identity) and HTTP (JWT/API-key) scenarios.
/// </summary>
public interface IUserContextProvider
{
    /// <summary>
    /// Returns the current user's validated identity and API-key capabilities.
    /// Throws <see cref="InvalidOperationException"/> if either cannot be resolved.
    /// </summary>
    Task<McpUserContext> GetCurrentContextAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the current user's ID.
    /// Throws <see cref="InvalidOperationException"/> if no user can be resolved.
    /// </summary>
    Task<Guid> GetCurrentUserIdAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to resolve the current user's ID without throwing.
    /// Returns null when no user can be resolved.
    /// </summary>
    Task<Guid?> GetUserIdAsync(CancellationToken cancellationToken = default);
}
