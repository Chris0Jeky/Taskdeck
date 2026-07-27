using System.Security.Cryptography;
using System.Text;

namespace Taskdeck.Application.Bootstrap;

/// <summary>
/// Provides the single cross-process lock identity used by every Taskdeck host that reads or writes a local
/// bootstrap configuration file. Locking by the canonical full path lets the API and CLI safely share the
/// same <c>appsettings.local.json</c> beside a database.
/// </summary>
public static class BootstrapFileLock
{
    /// <summary>Builds the stable OS mutex name for one canonical local-config path.</summary>
    public static string BuildMutexName(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        // Fold case conservatively on every platform. That can over-serialize two deliberately case-distinct
        // files on a case-sensitive volume, but it keeps textual aliases synchronized on Windows, default
        // macOS volumes, and case-insensitive Unix mounts. False contention is safer than split-lock writes.
        var normalized = Path.GetFullPath(path).ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        var hex = Convert.ToHexString(hash).AsSpan(0, 32);
        var prefix = OperatingSystem.IsWindows() ? "Global\\" : string.Empty;
        return $"{prefix}Taskdeck.FirstRun.{hex}";
    }

    /// <summary>
    /// Acquires the per-path cross-process mutex or fails before callers inspect, generate, or write a value.
    /// A false wait result is never treated as permission to continue unlocked.
    /// </summary>
    public static IDisposable Acquire(string path, TimeSpan timeout, Action? onContention = null)
    {
        Mutex? mutex = null;
        var acquired = false;
        try
        {
            mutex = new Mutex(initiallyOwned: false, name: BuildMutexName(path));
            try
            {
                acquired = mutex.WaitOne(TimeSpan.Zero);
                if (!acquired)
                {
                    try { onContention?.Invoke(); } catch { /* diagnostics must not alter lock safety */ }
                    acquired = mutex.WaitOne(timeout);
                }
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                throw new TimeoutException(
                    $"Timed out waiting for the cross-process bootstrap lock for {Path.GetFullPath(path)}; " +
                    "no value was read or written.");
            }

            return new Lease(mutex);
        }
        catch (TimeoutException)
        {
            mutex?.Dispose();
            throw;
        }
        catch (Exception ex) when (
            ex is UnauthorizedAccessException
                or WaitHandleCannotBeOpenedException
                or IOException)
        {
            mutex?.Dispose();
            throw new IOException(
                $"Cross-process bootstrap lock unavailable for {Path.GetFullPath(path)}; no value was written.",
                ex);
        }
    }

    private sealed class Lease(Mutex mutex) : IDisposable
    {
        private Mutex? _mutex = mutex;

        public void Dispose()
        {
            var owned = Interlocked.Exchange(ref _mutex, null);
            if (owned is null)
            {
                return;
            }

            try
            {
                owned.ReleaseMutex();
            }
            finally
            {
                owned.Dispose();
            }
        }
    }
}
