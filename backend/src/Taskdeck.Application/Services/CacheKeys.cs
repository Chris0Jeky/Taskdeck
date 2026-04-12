namespace Taskdeck.Application.Services;

/// <summary>
/// Centralized cache key definitions for the cache-aside pattern.
/// Keys are structured as "resource:scope:id" and prefixed by the
/// cache service with the global key prefix (e.g., "td:").
/// </summary>
public static class CacheKeys
{
    // NOTE: BoardDetail is intentionally NOT cached. BoardDetailDto includes columns
    // with card counts, and ColumnService/CardService mutate that data without cache
    // awareness. Caching board detail would serve stale column/card information.

    /// <summary>
    /// Cache key for a user's board list (default, non-filtered, non-archived).
    /// Format: boards:user:{userId}
    /// </summary>
    public static string BoardListForUser(Guid userId) => $"boards:user:{userId}";
}
