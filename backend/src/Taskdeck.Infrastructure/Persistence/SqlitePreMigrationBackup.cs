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
/// <b>Retention (#1839).</b> Snapshots are named
/// <c>&lt;db file name&gt;-pre-migration-&lt;UTC timestamp&gt;-&lt;sequence&gt;.db</c>. The key is
/// the database's full file name so two databases in one backup directory cannot prune each
/// other, and the sequence — one past the highest already on disk — is what retention orders by,
/// so a wall clock that steps backwards cannot make the newest snapshot look oldest and get it
/// deleted first. Snapshots written by the earlier, stem-keyed, sequence-less v0.1.0 scheme are
/// still recognised and still age out; see <see cref="EnumerateSnapshots"/>.
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
    /// Sortable, filename-safe UTC timestamp. Millisecond precision, and fixed width so it never
    /// changes the ordinal ordering of the file names. It is descriptive only: retention orders
    /// by the sequence number below, because a wall clock can go backwards (#1839).
    /// </summary>
    private const string TimestampFormat = "yyyyMMdd'T'HHmmssfff'Z'";

    /// <summary>
    /// Width of the monotonic sequence suffix. Six digits keep the common case aligned; the
    /// pattern accepts more, and ordering parses the digits as a number rather than comparing
    /// them as text, so overflowing the width is a cosmetic event and not an ordering bug.
    /// </summary>
    private const string SequenceFormat = "D6";

    /// <summary>
    /// Ordering rank given to a snapshot written by the pre-#1839 naming scheme, which has no
    /// sequence suffix. Every sequenced snapshot was necessarily written by newer code and is
    /// therefore newer, so legacy files sort oldest and age out first. See <see cref="Prune"/>.
    /// </summary>
    private const long LegacySequence = -1;

    /// <summary>
    /// Busy timeout executed as <c>PRAGMA busy_timeout</c> on the SOURCE connection — the live
    /// database being read — in <see cref="WriteSnapshot"/>. It governs how long that connection
    /// waits for a lock held by an in-flight writer in another Taskdeck process instead of
    /// failing immediately, and mirrors the default
    /// <see cref="DatabaseSettings.BusyTimeoutMilliseconds"/> so a moment of contention does not
    /// fail the whole startup closed. The destination connection deliberately gets no timeout:
    /// it writes a freshly created <c>.tmp</c> file that nothing else has opened, so there is
    /// nothing for it to contend with.
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
        var key = BuildKey(databaseFilePath);
        var legacyKey = BuildLegacyKey(databaseFilePath);

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

        string destinationPath;
        try
        {
            destinationPath = ReserveDestinationPath(directory, key, legacyKey);
        }
        catch (Exception ex) when (IsFileSystemFailure(ex))
        {
            // Fail closed: without a readable backup directory we cannot pick a name that is
            // guaranteed not to clobber an existing snapshot, and silently overwriting one would
            // destroy the protection this feature exists to provide.
            throw new PreMigrationBackupException(
                BuildFailureMessage(databaseFilePath, directory, ex),
                ex);
        }

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

        Prune(directory, key, legacyKey, settings.RetainCount, logger);

        return destinationPath;
    }

    /// <summary>
    /// The retention key for a database file: its FULL file name, extension included.
    /// </summary>
    /// <remarks>
    /// Keying on <see cref="Path.GetFileNameWithoutExtension(string)"/> (the pre-#1839 scheme)
    /// made <c>taskdeck.db</c> and <c>taskdeck.sqlite</c> share the key <c>taskdeck</c>, so two
    /// databases sitting in one directory — or pointed at one configured
    /// <see cref="DatabaseBackupSettings.Directory"/> — pruned each other's snapshots. The full
    /// file name is unique within a directory by definition, so it cannot collide.
    /// </remarks>
    internal static string BuildKey(string databaseFilePath) => Path.GetFileName(databaseFilePath);

    /// <summary>
    /// The pre-#1839 retention key (the file name stem), still recognised so snapshots written by
    /// shipped v0.1.0 code are pruned instead of accumulating forever.
    /// </summary>
    internal static string BuildLegacyKey(string databaseFilePath) =>
        Path.GetFileNameWithoutExtension(databaseFilePath);

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
    /// Picks the next free snapshot path: <c>&lt;db file name&gt;-pre-migration-&lt;UTC
    /// timestamp&gt;-&lt;sequence&gt;.db</c>.
    /// </summary>
    /// <remarks>
    /// The sequence is one past the highest sequence already present for this database in this
    /// directory, which makes it monotonic with respect to the snapshots that exist rather than
    /// to the wall clock. That is the whole point (#1839): the timestamp is written by
    /// <see cref="DateTimeOffset.UtcNow"/>, so an NTP correction or a VM restore can move it
    /// backwards, and a retention order derived from it would then rank the newest snapshot
    /// oldest and delete it first — exactly the file the user needs. Reading the sequence off the
    /// directory also survives a process restart, which an in-memory counter would not.
    /// </remarks>
    private static string ReserveDestinationPath(string directory, string key, string legacyKey)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString(TimestampFormat, CultureInfo.InvariantCulture);

        var sequence = 1L;
        foreach (var existing in EnumerateSnapshots(directory, key, legacyKey))
        {
            if (existing.Sequence >= sequence)
            {
                sequence = existing.Sequence + 1;
            }
        }

        while (true)
        {
            var candidate = Path.Combine(directory, BuildFileName(key, timestamp, sequence));

            if (!File.Exists(candidate) && !File.Exists(candidate + ".tmp"))
            {
                return candidate;
            }

            sequence++;
        }
    }

    private static string BuildFileName(string key, string timestamp, long sequence) =>
        key
        + FileNameMarker
        + timestamp
        + "-"
        + sequence.ToString(SequenceFormat, CultureInfo.InvariantCulture)
        + FileExtension;

    /// <summary>A managed snapshot file, with the two fields retention orders by.</summary>
    private readonly record struct Snapshot(string Path, string FileName, long Sequence, string Timestamp);

    /// <summary>
    /// Matches the current naming scheme for one database: full file name, marker, timestamp,
    /// sequence.
    /// </summary>
    private static Regex CurrentNamePattern(string key) => new(
        "^" + Regex.Escape(key + FileNameMarker) + @"(\d{8}T\d{9}Z)-(\d{6,})" + Regex.Escape(FileExtension) + "$",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// Matches the pre-#1839 naming scheme: file name stem, marker, timestamp, no sequence.
    /// </summary>
    private static Regex LegacyNamePattern(string legacyKey) => new(
        "^" + Regex.Escape(legacyKey + FileNameMarker) + @"(\d{8}T\d{9}Z)" + Regex.Escape(FileExtension) + "$",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// Finds every managed snapshot for one database in <paramref name="directory"/>, in BOTH the
    /// current and the pre-#1839 naming schemes, ordered newest first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Order.</b> Sequence descending first — that is the monotonic key, immune to a clock that
    /// steps backwards. The timestamp is only a tiebreaker for the legacy files, which have no
    /// sequence; among themselves the timestamp is the best signal available, and it is the same
    /// order the code that wrote them used.
    /// </para>
    /// <para>
    /// <b>Legacy compatibility.</b> Recognising the old shape is what stops v0.1.0's snapshots
    /// from accumulating forever once a host upgrades. One wart is inherited rather than
    /// introduced: because the old shape keys on the stem, a legacy snapshot of
    /// <c>taskdeck.db</c> is indistinguishable from a legacy snapshot of <c>taskdeck.sqlite</c>,
    /// so in the (rare) two-databases-one-directory case both databases will count the same
    /// legacy files as theirs. That ambiguity is bounded and self-clearing: no new file is ever
    /// written in the legacy shape, so it disappears as the old snapshots age out.
    /// </para>
    /// </remarks>
    private static List<Snapshot> EnumerateSnapshots(string directory, string key, string legacyKey)
    {
        var currentPattern = CurrentNamePattern(key);
        var legacyPattern = LegacyNamePattern(legacyKey);

        var globs = new List<string> { key + FileNameMarker + "*" + FileExtension };
        if (!string.Equals(key, legacyKey, StringComparison.Ordinal))
        {
            globs.Add(legacyKey + FileNameMarker + "*" + FileExtension);
        }

        // The two globs cannot overlap (one prefix is a proper extension of the other), but a
        // file system that reports a name twice must not produce a duplicate prune candidate.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var snapshots = new List<Snapshot>();

        foreach (var glob in globs)
        {
            foreach (var path in Directory.EnumerateFiles(directory, glob))
            {
                var fileName = Path.GetFileName(path);
                if (!seen.Add(fileName))
                {
                    continue;
                }

                var match = currentPattern.Match(fileName);
                if (match.Success)
                {
                    // An unparseable sequence means we cannot order the file, and a file we
                    // cannot order is a file we must not delete.
                    if (long.TryParse(
                            match.Groups[2].ValueSpan,
                            NumberStyles.None,
                            CultureInfo.InvariantCulture,
                            out var sequence))
                    {
                        snapshots.Add(new Snapshot(path, fileName, sequence, match.Groups[1].Value));
                    }

                    continue;
                }

                match = legacyPattern.Match(fileName);
                if (match.Success)
                {
                    snapshots.Add(new Snapshot(path, fileName, LegacySequence, match.Groups[1].Value));
                }
            }
        }

        snapshots.Sort(static (left, right) =>
        {
            var bySequence = right.Sequence.CompareTo(left.Sequence);
            if (bySequence != 0)
            {
                return bySequence;
            }

            var byTimestamp = string.CompareOrdinal(right.Timestamp, left.Timestamp);
            return byTimestamp != 0
                ? byTimestamp
                : string.CompareOrdinal(right.FileName, left.FileName);
        });

        return snapshots;
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
    /// this helper's own strict naming pattern FOR THIS DATABASE are considered, so neither an
    /// unrelated file nor another database's snapshots sharing the directory are ever deleted.
    /// "Newest" means highest sequence, not latest timestamp — see
    /// <see cref="EnumerateSnapshots"/>.
    /// </summary>
    private static void Prune(string directory, string key, string legacyKey, int retainCount, ILogger? logger)
    {
        List<Snapshot> snapshots;
        try
        {
            // Newest first: index 0 is the highest sequence, so Skip(retainCount) is exactly the
            // set that must go.
            snapshots = EnumerateSnapshots(directory, key, legacyKey);
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

        foreach (var stale in snapshots.Skip(retainCount).Select(snapshot => snapshot.Path))
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
