using System.ComponentModel.DataAnnotations;

namespace Taskdeck.Application.Services;

/// <summary>
/// Configuration for the pre-migration SQLite auto-backup (#1803).
/// Bound from appsettings.json <c>"Database:Backup"</c>.
/// <para>
/// Taskdeck is local-first: the user's entire workspace is one SQLite file, and a schema
/// migration is the only routine operation that rewrites it in place. Before applying any
/// pending migration the host takes a consistent snapshot of that file so an upgrade that
/// goes wrong is recoverable by copying one file back.
/// </para>
/// <para>
/// <b>Fail-closed.</b> When a backup is required but cannot be written, startup fails and the
/// migration is NOT applied. That is deliberate: a failed upgrade with a backup is an
/// inconvenience, a failed upgrade without one is data loss.
/// </para>
/// </summary>
public sealed class DatabaseBackupSettings
{
    /// <summary>
    /// Whether to snapshot the SQLite database file before applying pending migrations.
    /// Default <c>true</c>.
    /// <para>
    /// Setting this to <c>false</c> disables the only automatic protection against a failed
    /// upgrade. Do it only when an external backup system already snapshots the database file,
    /// or for throwaway/ephemeral databases.
    /// </para>
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How many pre-migration backups to keep for a given database file. After a successful
    /// backup, older ones beyond this count are deleted oldest-first. Default <c>5</c>.
    /// </summary>
    [Range(1, 100, ErrorMessage = "Database:Backup:RetainCount must be between 1 and 100.")]
    public int RetainCount { get; set; } = 5;

    /// <summary>
    /// Directory the backups are written to. When null or empty, a <c>backups</c> folder next
    /// to the database file is used. A relative path is resolved against the directory holding
    /// the database file (not the process working directory), so the location stays stable
    /// across the API, CLI, and MCP hosts, which do not share a working directory.
    /// </summary>
    public string? Directory { get; set; }
}
