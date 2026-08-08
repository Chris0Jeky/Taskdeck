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
/// parallelism override, so that call was **shared mutable state reachable from every test class at
/// once**. A victim failed with <c>ObjectDisposedException: 'SQLitePCL.sqlite3'</c> inside
/// <c>SqliteConnection.Open()</c> on a migration's first connection, in a PR that changed no backend
/// code (<c>#1608</c>), and the same shape has been investigated as a one-off three times before
/// (<c>#1282</c>, <c>#1357</c>, <c>#1512</c>).
/// </para>
/// <para>
/// **Stated precisely, because the exact window was NOT reproduced:** an adversarial review probed
/// Microsoft.Data.Sqlite 8.0.29 directly and found that the pool-clearing call does *not* dispose a
/// connection that is currently checked out, and could not provoke the failure in ~233,000 racing
/// cycles. So the stack trace is *consistent with* a pooled native handle being disposed under a
/// concurrent open, and the call was undeniably process-global shared state that no test owned — but
/// the precise interleaving is inferred, not demonstrated. Do not repeat it downstream as
/// established mechanism. What is established is that the shared state existed, that the failure was
/// real and unrelated to the diff that surfaced it, and that removing the state removes the class of
/// problem.
/// </para>
/// <para>
/// With pooling disabled the handle closes when the connection is disposed, the <c>-wal</c>/<c>-shm</c>
/// sidecars unlock, cleanup can delete them, and there is no pool to clear — so the shared mutable
/// state is removed rather than timed. Do not reintroduce the pool-clearing call: it would restore
/// the shared state for every SQLite test in this assembly, not only the one that calls it.
/// </para>
/// <para>
/// This does **not** make the assembly leak-free, and the change should not be described as if it
/// did. Measured on a full-suite run after the change: zero <c>-wal</c>/<c>-shm</c> leaks, but ~199
/// zero-byte <c>.db.migrate.lock</c> files still accumulate, because
/// <c>TestWebApplicationFactory.GetDatabaseCleanupTargets</c> enumerates
/// <c>.db</c>/<c>-wal</c>/<c>-shm</c>/<c>-journal</c> and never <c>.migrate.lock</c> —
/// a pre-existing gap unrelated to pooling (<c>SerializedMigratorTests</c> and the CLI harness do
/// clean it). One real <c>.db</c> also survived. Tracked separately; see <c>#1609</c>.
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
