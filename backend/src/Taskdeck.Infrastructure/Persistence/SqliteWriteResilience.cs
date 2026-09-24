using Microsoft.Data.Sqlite;

namespace Taskdeck.Infrastructure.Persistence;

/// <summary>
/// Retries set-based write statements (ExecuteUpdate/ExecuteDelete) across transient SQLite
/// writer locks. Mirrors the retry policy embedded in UnitOfWork.SaveChangesAsync: those
/// statements bypass SaveChanges, so without this wrapper a contended writer slot surfaces
/// SQLITE_BUSY as a failure instead of riding out the lock like tracked saves do.
/// UnitOfWork.SaveChangesAsync shares this same policy (detection, attempts, backoff)
/// so there is exactly one definition of a transient SQLite write lock.
/// </summary>
public static class SqliteWriteResilience
{
    internal const int MaxWriteLockRetries = 5;

    public static async Task<T> ExecuteWithWriteLockRetryAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await operation(cancellationToken);
            }
            catch (Exception exception) when (IsTransientWriteLock(exception) && attempt < MaxWriteLockRetries)
            {
                await Task.Delay(GetWriteLockRetryDelay(attempt), cancellationToken);
            }
        }
    }

    public static bool IsTransientWriteLock(Exception exception)
    {
        // Walk from the exception itself: SaveChanges failures arrive wrapped in
        // DbUpdateException, while set-based statements may surface the provider
        // exception directly. Matches UnitOfWork's SQLITE_BUSY/LOCKED signatures.
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqliteException sqliteException
                && (sqliteException.SqliteErrorCode == 5 || sqliteException.SqliteErrorCode == 6))
            {
                return true;
            }

            if (current.Message.Contains("database is locked", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("database table is locked", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    internal static TimeSpan GetWriteLockRetryDelay(int attempt)
    {
        var multiplier = attempt + 1;
        return TimeSpan.FromMilliseconds(25 * multiplier * multiplier);
    }
}
