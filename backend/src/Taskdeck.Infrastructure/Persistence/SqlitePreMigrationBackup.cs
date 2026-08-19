using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Taskdeck.Application.Services;

namespace Taskdeck.Infrastructure.Persistence;

/// <summary>
/// Takes a consistent snapshot of the SQLite database file immediately before EF Core applies
/// pending migrations, and prunes older snapshots down to the configured retention (#1803).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the backup API and not a file copy.</b> Taskdeck runs SQLite in WAL mode (#1130), so
/// the durable state of the database is spread across <c>&lt;db&gt;</c>, <c>&lt;db&gt;-wal</c>,
/// and <c>&lt;db&gt;-shm</c>. Copying only the main file silently drops every committed
/// transaction still sitting in an uncheckpointed WAL, and copying all three is racy while any
/// connection is open. This helper uses SQLite's online backup API
/// (<see cref="SqliteConnection.BackupDatabase(SqliteConnection)"/>), which is WAL-aware by
/// construction: it reads a single consistent snapshot through a read transaction, folding the
/// WAL contents in, and writes one self-contained destination file. #1166 reached the same
/// conclusion for the dev-sandbox export/import path.
/// </para>
/// <para>
/// <b>Ordering.</b> The caller (<see cref="SerializedMigrator"/>) runs this while holding the
/// cross-process migration lock and BEFORE <c>Database.Migrate()</c> is called, so no migrator
/// has opened a write connection for DDL yet. The snapshot therefore captures the pre-migration
/// schema and data. Ordinary application writes may still be in flight from other processes;
/// the backup API handles those correctly (it snapshots a committed state), which is exactly
/// why a raw copy is not used.
/// </para>
/// <para>
/// <b>Atomicity.</b> The snapshot is written to a <c>.tmp</c> sibling and moved into place only
/// after it is complete and its WAL has been checkpointed away, so a crashed or failed backup
/// can never leave a truncated file under a name that looks like a usable backup.
/// </para>
/// <para>
/// <b>Fail-closed.</b> Any failure to produce the snapshot throws
/// <see cref="PreMigrationBackupException"/>, which propagates out of startup and prevents the
/// migration. Retention pruning is deliberately NOT fail-closed: the fresh backup already
/// exists, and refusing to start because a stale backup could not be deleted would trade a real
/// protection for a cosmetic one. Pruning failures are logged as warnings, never swallowed
/// silently.
/// </para>
/// </remarks>
internal static class SqlitePreMigrationBackup
{
    /// <summary>Folder created next to the database file when no directory is configured.</summary>
    internal const string DefaultDirectoryName = "backups";

    /// <summary>Infix that marks a file as one of our managed pre-migration snapshots.</summary>
    internal const string FileNameMarker = "-pre-migration-";

    internal const string FileExtension = ".db";

    /// <summary>
    /// Sortable, filename-safe UTC timestamp. Millisecond precision keeps the lexical order of
    /// the file names identical to their chronological order, which is what retention pruning
    /// relies on (file-system timestamps are not dependable enough for ordering).
    /// </summary>
    private const string TimestampFormat = "yyyyMMdd'T'HHmmssfff'Z'";

    /// <summary>
    /// Busy timeout applied to the snapshot's source connection. The backup reads through a
    /// shared lock and can contend with an in-flight writer from another Taskdeck process; this
    /// mirrors the default <see cref="DatabaseSettings.BusyTimeoutMilliseconds"/> so a moment of
    /// contention waits instead of failing the whole startup closed.
    /// </summary>
    private const int SourceBusyTimeoutMilliseconds = 5000;

    /// <summary>
    /// Writes a snapshot of <paramref name="databaseFilePath"/> and prunes older snapshots to
    /// <see cref="DatabaseBackupSettings.RetainCount"/>.
    /// </summary>
    /// <returns>The absolute path of the snapshot that was written.</returns>
    /// <exception cref="PreMigrationBackupException">
    /// The snapshot could not be written. The caller must not migrate.
    /// </exception>
    internal static string Create(
        string databaseFilePath,
        DatabaseBackupSettings settings,
        ILogger? logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseFilePath);
        ArgumentNullException.ThrowIfNull(settings);

        var directory = ResolveBackupDirectory(databaseFilePath, settings);
        var stem = Path.GetFileNameWithoutExtension(databaseFilePath);

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex) when (IsFileSystemFailure(ex))
        {
            throw new PreMigrationBackupException(
                BuildFailureMessage(databaseFilePath, directory, ex),
                ex);
        }

        var destinationPath = ReserveDestinationPath(directory, stem);
        var temporaryPath = destinationPath + ".tmp";

        try
        {
            WriteSnapshot(databaseFilePath, temporaryPath);
            File.Move(temporaryPath, destinationPath);
        }
        catch (Exception ex) when (IsFileSystemFailure(ex) || ex is SqliteException or InvalidOperationException)
        {
            CleanupPartialSnapshot(temporaryPath, logger);
            throw new PreMigrationBackupException(
                BuildFailureMessage(databaseFilePath, directory, ex),
                ex);
        }

        logger?.LogInformation(
            "Pre-migration backup written to '{BackupPath}' before applying pending migrations to " +
            "'{DatabasePath}'.",
            destinationPath,
            databaseFilePath);

        Prune(directory, stem, settings.RetainCount, logger);

        return destinationPath;
    }

    /// <summary>
    /// Resolves the directory snapshots are written to: the configured directory (relative paths
    /// resolved against the database file's own directory, so every host mode agrees), or a
    /// <c>backups</c> folder beside the database file.
    /// </summary>
    internal static string ResolveBackupDirectory(string databaseFilePath, DatabaseBackupSettings settings)
    {
        var databaseDirectory = Path.GetDirectoryName(Path.GetFullPath(databaseFilePath));
        if (string.IsNullOrEmpty(databaseDirectory))
        {
            // A rooted path always has a parent; this only guards a pathological input.
            databaseDirectory = Directory.GetCurrentDirectory();
        }

        var configured = settings.Directory;
        if (string.IsNullOrWhiteSpace(configured))
        {
            return Path.Combine(databaseDirectory, DefaultDirectoryName);
        }

        return Path.IsPathRooted(configured)
            ? Path.GetFullPath(configured)
            : Path.GetFullPath(Path.Combine(databaseDirectory, configured));
    }

    /// <summary>
    /// Picks the next free snapshot path. On a collision the timestamp is advanced by a
    /// millisecond rather than suffixed, so file names stay strictly increasing in lexical order
    /// and retention pruning can order by name.
    /// </summary>
    private static string ReserveDestinationPath(string directory, string stem)
    {
        var timestamp = DateTimeOffset.UtcNow;
        while (true)
        {
            var candidate = Path.Combine(
                directory,
                stem + FileNameMarker + timestamp.ToString(TimestampFormat, CultureInfo.InvariantCulture) + FileExtension);

            if (!File.Exists(candidate) && !File.Exists(candidate + ".tmp"))
            {
                return candidate;
            }

            timestamp = timestamp.AddMilliseconds(1);
        }
    }

    /// <summary>
    /// Copies the live database into <paramref name="temporaryPath"/> through SQLite's online
    /// backup API, then checkpoints and truncates the destination's WAL so the resulting file is
    /// standalone — "backup = copy this one file" only holds if there are no sidecars to forget.
    /// </summary>
    private static void WriteSnapshot(string databaseFilePath, string temporaryPath)
    {
        DeleteIfExists(temporaryPath);
        DeleteIfExists(temporaryPath + "-wal");
        DeleteIfExists(temporaryPath + "-shm");

        var sourceConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databaseFilePath,
            // ReadWrite, never ReadWriteCreate: the caller has already established the file
            // exists, and Create would mask a wrong path by snapshotting an empty database.
            Mode = SqliteOpenMode.ReadWrite,
            // Pooling off so the handle (and the -wal/-shm locks it holds on Windows) is released
            // the moment this method returns, well before the migrator opens its write connection.
            Pooling = false,
        }.ConnectionString;

        var destinationConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = temporaryPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ConnectionString;

        using var source = new SqliteConnection(sourceConnectionString);
        source.Open();
        Execute(source, $"PRAGMA busy_timeout={SourceBusyTimeoutMilliseconds}");

        using var destination = new SqliteConnection(destinationConnectionString);
        destination.Open();

        source.BackupDatabase(destination);

        // The backup copies page 1 verbatim, so a WAL-mode source yields a WAL-mode destination.
        // Fold that WAL back into the main file before closing so the snapshot is a single file.
        // On a non-WAL destination this is a harmless no-op.
        Execute(destination, "PRAGMA wal_checkpoint(TRUNCATE)");
        Execute(destination, "PRAGMA journal_mode=DELETE");
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        // ExecuteScalar, not ExecuteNonQuery: PRAGMA journal_mode and wal_checkpoint both return
        // a result row, and discarding it here keeps the call uniform for plain PRAGMAs too.
        command.ExecuteScalar();
    }

    /// <summary>
    /// Deletes snapshots beyond <paramref name="retainCount"/>, newest kept. Only files matching
    /// this helper's own strict naming pattern are considered, so an unrelated file that happens
    /// to sit in the backup directory is never deleted.
    /// </summary>
    private static void Prune(string directory, string stem, int retainCount, ILogger? logger)
    {
        var pattern = new Regex(
            "^" + Regex.Escape(stem + FileNameMarker) + @"\d{8}T\d{9}Z" + Regex.Escape(FileExtension) + "$",
            RegexOptions.CultureInvariant);

        List<string> snapshots;
        try
        {
            snapshots = Directory
                .EnumerateFiles(directory, stem + FileNameMarker + "*" + FileExtension)
                .Where(path => pattern.IsMatch(Path.GetFileName(path)))
                // Names embed a fixed-width sortable UTC timestamp, so ordinal order IS
                // chronological order. Descending: index 0 is the newest.
                .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex) when (IsFileSystemFailure(ex))
        {
            logger?.LogWarning(
                ex,
                "Pre-migration backup retention could not enumerate '{BackupDirectory}' ({Reason}); " +
                "the new backup was written, but older backups were not pruned.",
                directory,
                ex.GetType().Name);
            return;
        }

        foreach (var stale in snapshots.Skip(retainCount))
        {
            try
            {
                File.Delete(stale);
                logger?.LogDebug("Pruned pre-migration backup '{BackupPath}' (retain {RetainCount}).", stale, retainCount);
            }
            catch (Exception ex) when (IsFileSystemFailure(ex))
            {
                // Never fail-closed on pruning: the protective snapshot already exists, and
                // refusing to start over an undeletable stale file would be strictly worse.
                logger?.LogWarning(
                    ex,
                    "Could not prune stale pre-migration backup '{BackupPath}' ({Reason}); it will be " +
                    "retried on the next migration.",
                    stale,
                    ex.GetType().Name);
            }
        }
    }

    private static void CleanupPartialSnapshot(string temporaryPath, ILogger? logger)
    {
        foreach (var path in new[] { temporaryPath, temporaryPath + "-wal", temporaryPath + "-shm" })
        {
            try
            {
                DeleteIfExists(path);
            }
            catch (Exception ex) when (IsFileSystemFailure(ex))
            {
                logger?.LogWarning(
                    ex,
                    "A failed pre-migration backup left '{PartialPath}' behind and it could not be " +
                    "removed ({Reason}); delete it manually. It is NOT a usable backup.",
                    path,
                    ex.GetType().Name);
            }
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static bool IsFileSystemFailure(Exception ex) =>
        ex is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or PathTooLongException
            or DirectoryNotFoundException
            or System.Security.SecurityException;

    private static string BuildFailureMessage(string databaseFilePath, string directory, Exception ex) =>
        $"Taskdeck could not back up the database '{databaseFilePath}' before applying pending " +
        $"migrations, so the migration was NOT applied and startup is stopping. " +
        $"Backup directory: '{directory}'. Cause: {ex.GetType().Name}: {ex.Message} " +
        "Fix the cause (usually free disk space, or grant the Taskdeck process write access to " +
        "that directory), or set 'Database:Backup:Directory' to a writable location. " +
        "Only after you have copied the database file somewhere safe yourself may you set " +
        "'Database:Backup:Enabled' to false to bypass this check.";
}
