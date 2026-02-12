namespace Taskdeck.Application.Interfaces;

/// <summary>
/// Provides access to the current authenticated user's identity from JWT claims.
/// This is the single source of truth for actor identity - never trust client-supplied user IDs.
/// </summary>
public interface IUserContext
{
    /// <summary>
    /// Gets the current authenticated user's ID from JWT claims.
    /// Returns null if no user is authenticated.
    /// </summary>
    string? UserId { get; }

    /// <summary>
    /// Gets whether a user is currently authenticated.
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Gets the current user's role from JWT claims.
    /// Returns null if no role is present.
    /// </summary>
    string? Role { get; }
}
