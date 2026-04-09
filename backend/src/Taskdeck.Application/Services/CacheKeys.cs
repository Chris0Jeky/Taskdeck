namespace Taskdeck.Application.Services;

/// <summary>
/// Centralized cache key definitions for the cache-aside pattern.
/// Keys are structured as "resource:scope:id" and prefixed by the
/// cache service with the global key prefix (e.g., "td:").
/// </summary>
public static class CacheKeys
{
    /// <summary>
    /// Cache key for board detail (includes columns).
    /// Format: board:{boardId}:detail
    /// </summary>
    public static string BoardDetail(Guid boardId) => $"board:{boardId}:detail";

    /// <summary>
    /// Cache key for a user's board list (default, non-filtered, non-archived).
    /// Format: boards:user:{userId}
    /// </summary>
    public static string BoardListForUser(Guid userId) => $"boards:user:{userId}";
}
