using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Taskdeck.Infrastructure.Persistence;

/// <summary>
/// Applies the SQLite PRAGMAs Taskdeck needs for safe local-first concurrency on
/// every connection open:
/// <list type="bullet">
///   <item><c>journal_mode=WAL</c> — write-ahead logging lets many readers run
///   concurrently with a single writer (the API UI, an MCP agent, and the CLI all
///   open the same file), instead of readers and writers blocking each other.</item>
///   <item><c>busy_timeout</c> — when the single writer slot is contended, a second
///   writer waits up to this many milliseconds for the lock to free instead of
///   failing immediately with <c>SQLITE_BUSY</c> ("database is locked").</item>
/// </list>
/// <para>
/// <c>journal_mode</c> is persisted in the database header, so re-issuing it on each
/// open is an idempotent no-op after the first writer sets it; <c>busy_timeout</c> is a
/// per-connection setting and therefore must be set on every connection. On an in-memory
/// database (<c>Data Source=:memory:</c>) WAL is unsupported and the PRAGMA is a harmless
/// no-op, so this interceptor is safe to register unconditionally.
/// </para>
/// </summary>
public sealed class SqlitePragmaConnectionInterceptor : DbConnectionInterceptor
{
    private readonly int _busyTimeoutMilliseconds;

    public SqlitePragmaConnectionInterceptor(int busyTimeoutMilliseconds)
    {
        _busyTimeoutMilliseconds = busyTimeoutMilliseconds;
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ApplyPragmas(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await ApplyPragmasAsync(connection, cancellationToken).ConfigureAwait(false);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken).ConfigureAwait(false);
    }

    private void ApplyPragmas(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = BuildPragmaSql();
        command.ExecuteNonQuery();
    }

    private async Task ApplyPragmasAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = BuildPragmaSql();
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    // busy_timeout is interpolated (not parameterized) because PRAGMA statements do not
    // accept bound parameters in SQLite. The value originates from validated configuration
    // (DatabaseSettings.BusyTimeoutMilliseconds, Range-checked) and is an int, so there is
    // no injection surface.
    private string BuildPragmaSql() =>
        $"PRAGMA journal_mode=WAL; PRAGMA busy_timeout={_busyTimeoutMilliseconds};";
}
