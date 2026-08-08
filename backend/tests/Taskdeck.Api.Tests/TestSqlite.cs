namespace Taskdeck.Api.Tests;

/// <summary>
/// Builds SQLite connection strings for file-backed tests, with pooling disabled.
/// </summary>
/// <remarks>
/// <para>
/// Pooling is off deliberately (<c>#1609</c>), and this helper exists so that decision lives in one
/// place instead of in a dozen interpolated strings.
/// </para>
/// <para>
/// These tests each create their own <c>{Guid}.db</c> under the temp directory, so they look
/// mutually isolated. They were not. On Windows a pooled connection keeps the <c>.db</c>/<c>-wal</c>/
/// <c>-shm</c> files locked after the <see cref="Microsoft.EntityFrameworkCore.DbContext"/> is
/// disposed, so cleanup could not delete them — and the fix reached for was the pool-clearing API,
/// which is <b>process-global</b>: it disposes the pooled connections of every SQLite database in
/// the process, not just the caller's.
/// </para>
/// <para>
/// xUnit runs test classes as parallel collections by default and this assembly declares no
/// parallelism override, so one class's cleanup could dispose a native handle another class had
/// just taken from the pool and was about to open. The victim failed with
/// <c>ObjectDisposedException: 'SQLitePCL.sqlite3'</c> inside <c>SqliteConnection.Open()</c> —
/// typically on the first connection a migration opens, which is why it surfaced as an unrelated
/// integration test failing at random.
/// </para>
/// <para>
/// With pooling disabled the handle closes when the connection is disposed, the files unlock
/// immediately, cleanup can delete them, and there is no pool to clear — so the shared mutable
/// state is removed rather than timed. Do not reintroduce the pool-clearing call: it would restore
/// the race for every SQLite test in this assembly, not only the one that calls it.
/// </para>
/// <para>
/// In-memory connection strings (<c>Data Source=:memory:</c>) deliberately do not use this helper.
/// They back no file, so they never needed the pool cleared, and their lifetime semantics are the
/// test's own concern.
/// </para>
/// </remarks>
internal static class TestSqlite
{
    /// <summary>
    /// Returns a file-backed SQLite connection string with connection pooling disabled.
    /// </summary>
    /// <param name="dbPath">Absolute path to the test's own database file.</param>
    internal static string ConnectionString(string dbPath) =>
        $"Data Source={dbPath};Pooling=False";
}
