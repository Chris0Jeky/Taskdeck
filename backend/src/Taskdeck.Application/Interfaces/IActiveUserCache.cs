namespace Taskdeck.Application.Interfaces;

/// <summary>
/// Cache for user active-status checks used by middleware to avoid per-request DB hits.
/// Implementations should be registered as singletons.
/// </summary>
public interface IActiveUserCache
{
    /// <summary>
    /// Returns whether the user is active. Uses cached value if available and within TTL;
    /// otherwise returns null to indicate a cache miss (caller should query the DB).
    /// </summary>
    bool? GetCachedActiveStatus(Guid userId);

    /// <summary>
    /// Stores the active status for a user with the configured TTL.
    /// </summary>
    void SetActiveStatus(Guid userId, bool isActive);

    /// <summary>
    /// Immediately removes the cached entry for the specified user, forcing the next
    /// request to re-check the database. Call this on account deletion/deactivation.
    /// </summary>
    void Invalidate(Guid userId);
}
