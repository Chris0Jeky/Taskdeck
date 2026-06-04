using System.ComponentModel.DataAnnotations;

namespace Taskdeck.Application.Services;

/// <summary>
/// Configuration for DbContext connection resilience.
/// Bound from appsettings.json "Database" section.
/// </summary>
public sealed class DatabaseSettings
{
    /// <summary>
    /// Command timeout in seconds for database operations.
    /// Applied to <c>SqliteDbContextOptionsBuilder.CommandTimeout</c>.
    /// This affects all EF Core commands including migrations
    /// (<c>Database.Migrate()</c>). Avoid setting very low values (e.g. 1s)
    /// if schema migrations are expected.
    /// </summary>
    [Range(1, 300, ErrorMessage = "CommandTimeoutSeconds must be between 1 and 300.")]
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// SQLite <c>busy_timeout</c> in milliseconds, applied to every connection.
    /// When the single SQLite writer slot is contended (the UI, an MCP agent, and
    /// the CLI share one file), a waiting connection retries for up to this long
    /// before surfacing <c>SQLITE_BUSY</c> ("database is locked"). Combined with
    /// WAL journal mode this keeps the local-first agent+UI workflow from breaking
    /// under normal concurrency. Set to 0 to fail immediately (not recommended).
    /// </summary>
    [Range(0, 60000, ErrorMessage = "BusyTimeoutMilliseconds must be between 0 and 60000.")]
    public int BusyTimeoutMilliseconds { get; set; } = 5000;
}
