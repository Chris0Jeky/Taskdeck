using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging;
using Taskdeck.Application.Services;
using Taskdeck.Infrastructure.Persistence;
using Taskdeck.Tests.Support;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Verifies the pre-migration SQLite auto-backup (#1803): a consistent snapshot is written
/// BEFORE pending migrations are applied, retention prunes older snapshots, an up-to-date
/// database is not copied on every boot, and a backup that cannot be written blocks the
/// migration instead of proceeding unprotected.
/// </summary>
/// <remarks>
/// Every test owns an isolated directory containing its own database file, so the default
/// "<c>backups</c> folder next to the database" location is exercised without tests colliding
/// in a shared temp folder.
/// </remarks>
public sealed class PreMigrationBackupTests : IDisposable
{
    private const int BusyTimeoutMs = 5000;

    private readonly string _root;
    private readonly string _dbPath;

    public PreMigrationBackupTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"taskdeck-premigration-backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "taskdeck.db");
    }

    private string DefaultBackupDirectory => Path.Combine(_root, SqlitePreMigrationBackup.DefaultDirectoryName);

    private TaskdeckDbContext NewFileContext()
    {
        var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
            .UseSqlite(TestSqlite.ConnectionString(_dbPath))
            // Mirror production: WAL + busy_timeout (#1130) on every connection. WAL is exactly
            // the condition that makes a naive file copy unsafe, so the tests must run under it.
            .AddInterceptors(new SqlitePragmaConnectionInterceptor(BusyTimeoutMs))
            .Options;
        return new TaskdeckDbContext(options);
    }

    // ── Backup happens, and happens BEFORE the migration ─────────────────────

    [Fact]
    public void Backup_captures_the_pre_migration_state_before_pending_migrations_are_applied()
    {
        var lastMigration = MigrateToOneBeforeLatest();
        SeedPreMigrationRow();

        var logger = new InMemoryLogger<PreMigrationBackupTests>();
        using (var context = NewFileContext())
        {
            context.Database.GetPendingMigrations().Should().Contain(
                lastMigration, "the fixture must leave exactly the final migration pending");

            SerializedMigrator.Migrate(context, new DatabaseBackupSettings(), logger);
        }

        var backups = ListBackups(DefaultBackupDirectory);
        backups.Should().ContainSingle("one pending-migration run must produce exactly one backup");

        // The live database moved forward...
        ReadAppliedMigrationIds(_dbPath).Should().Contain(
            lastMigration, "the migration must actually have been applied after the backup");

        // ...but the snapshot is pinned to the state BEFORE that migration. This is the ordering
        // assertion that matters: a backup taken after the migration would be worthless for
        // recovering from a bad upgrade, and would pass a mere "a file exists" check.
        var snapshotMigrations = ReadAppliedMigrationIds(backups[0]);
        snapshotMigrations.Should().NotContain(
            lastMigration,
            "the snapshot must capture the schema as it was BEFORE the pending migration ran");

        // And it captured the user's data, not just an empty shell.
        CountRows(backups[0], "BackupFixture").Should().Be(
            1, "the snapshot must contain the data that existed before the upgrade");

        logger.Entries.Should().Contain(
            e => e.Level == LogLevel.Information && e.Message.Contains("Pre-migration backup written"),
            "taking a backup is a user-visible upgrade event and must be logged");
    }

    [Fact]
    public void Backup_captures_rows_still_sitting_in_an_uncheckpointed_wal()
    {
        // The WAL-safety claim, made falsifiable. A writer connection is held OPEN across the
        // backup, so its committed row is still in <db>-wal and has not been checkpointed into
        // the main file. A raw copy of the main file would silently lose this row; the SQLite
        // online backup API must not.
        CreateStandaloneWalDatabase();

        using var writer = new SqliteConnection(TestSqlite.ConnectionString(_dbPath));
        writer.Open();
        Execute(writer, "PRAGMA busy_timeout=" + BusyTimeoutMs);
        Execute(writer, "INSERT INTO Notes (Body) VALUES ('written-into-the-wal')");

        File.Exists(_dbPath + "-wal").Should().BeTrue(
            "the fixture must actually be exercising WAL mode, otherwise it proves nothing");
        new FileInfo(_dbPath + "-wal").Length.Should().BeGreaterThan(
            0, "the committed row must still be uncheckpointed in the WAL while the writer is open");

        var backupPath = SqlitePreMigrationBackup.Create(_dbPath, new DatabaseBackupSettings(), logger: null);

        CountRows(backupPath, "Notes").Should().Be(
            1, "the snapshot must include committed rows that are still only in the WAL");
    }

    [Fact]
    public void Backup_file_is_standalone_with_no_wal_or_shm_sidecars()
    {
        // "Backup = copy this one file" (UPGRADING.md) is only true if the snapshot has no
        // sidecars a user could forget to copy.
        CreateStandaloneWalDatabase();
        InsertNote("checkpointed-before-backup");

        var backupPath = SqlitePreMigrationBackup.Create(_dbPath, new DatabaseBackupSettings(), logger: null);

        File.Exists(backupPath + "-wal").Should().BeFalse("the snapshot must not need a -wal sidecar");
        File.Exists(backupPath + "-shm").Should().BeFalse("the snapshot must not need a -shm sidecar");
        File.Exists(backupPath + ".tmp").Should().BeFalse("the staging file must be moved, not left behind");
        CountRows(backupPath, "Notes").Should().Be(1, "the standalone snapshot must still be readable");
    }

    // ── Retention ────────────────────────────────────────────────────────────

    [Fact]
    public void Retention_keeps_only_the_newest_backups()
    {
        CreateStandaloneWalDatabase();
        var settings = new DatabaseBackupSettings { RetainCount = 2 };

        var created = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            created.Add(SqlitePreMigrationBackup.Create(_dbPath, settings, logger: null));
        }

        var remaining = ListBackups(DefaultBackupDirectory);
        remaining.Should().HaveCount(2, "retention must prune down to RetainCount");
        remaining.Select(Path.GetFileName).Should().BeEquivalentTo(
            created.TakeLast(2).Select(Path.GetFileName),
            "the two NEWEST backups must survive — pruning the newest would defeat the purpose");
    }

    [Fact]
    public void Retention_never_deletes_unrelated_files_in_the_backup_directory()
    {
        CreateStandaloneWalDatabase();
        Directory.CreateDirectory(DefaultBackupDirectory);

        // Decoy 1: a plainly unrelated file. Cheap, but it does not reach the naming check —
        // the directory glob already excludes it.
        var manualCopy = Path.Combine(DefaultBackupDirectory, "my-own-manual-copy.db");
        File.WriteAllText(manualCopy, "not ours");

        // Decoys 2 and 3 are the ones that discriminate (#1839): both are inside the glob
        // `<db file name>-pre-migration-*.db`, so only the strict name pattern can save them.
        // The first has no timestamp at all; the second has a valid timestamp but no sequence
        // suffix — the shape a partially-implemented rename would produce.
        var shapedDecoy = Path.Combine(
            DefaultBackupDirectory,
            Path.GetFileName(_dbPath) + SqlitePreMigrationBackup.FileNameMarker + "hand-made-copy"
                + SqlitePreMigrationBackup.FileExtension);
        File.WriteAllText(shapedDecoy, "not ours either");

        var unsequencedDecoy = Path.Combine(
            DefaultBackupDirectory,
            Path.GetFileName(_dbPath) + SqlitePreMigrationBackup.FileNameMarker + "20200101T000000000Z"
                + SqlitePreMigrationBackup.FileExtension);
        File.WriteAllText(unsequencedDecoy, "still not ours");

        var settings = new DatabaseBackupSettings { RetainCount = 1 };
        var first = SqlitePreMigrationBackup.Create(_dbPath, settings, logger: null);
        var second = SqlitePreMigrationBackup.Create(_dbPath, settings, logger: null);

        File.Exists(manualCopy).Should().BeTrue(
            "pruning must only ever touch files matching the managed snapshot naming pattern");
        File.Exists(shapedDecoy).Should().BeTrue(
            "a file inside the enumerate glob but outside the strict name pattern must survive");
        File.Exists(unsequencedDecoy).Should().BeTrue(
            "a well-formed timestamp is not enough — the managed pattern requires the sequence too");

        File.Exists(first).Should().BeFalse("retention of 1 must prune the older managed snapshot");
        File.Exists(second).Should().BeTrue("retention of 1 must keep the newest managed snapshot");
    }

    [Fact]
    public void Retention_keys_on_the_full_file_name_so_a_sibling_database_is_not_pruned()
    {
        // `taskdeck.db` and `taskdeck.sqlite` share the stem "taskdeck" and, by default, the same
        // `backups` folder. Keying retention on the stem (#1839) made each database's prune count
        // the other's snapshots against its own RetainCount and delete them.
        var siblingDbPath = Path.Combine(_root, "taskdeck.sqlite");
        CreateStandaloneWalDatabase();
        CreateStandaloneWalDatabase(siblingDbPath);

        var settings = new DatabaseBackupSettings { RetainCount = 2 };
        SqlitePreMigrationBackup.ResolveBackupDirectory(siblingDbPath, settings).Should().Be(
            DefaultBackupDirectory,
            "the fixture only proves anything if both databases share one backup directory");

        var primary = new List<string>();
        var sibling = new List<string>();
        for (var i = 0; i < 2; i++)
        {
            primary.Add(SqlitePreMigrationBackup.Create(_dbPath, settings, logger: null));
            sibling.Add(SqlitePreMigrationBackup.Create(siblingDbPath, settings, logger: null));
        }

        foreach (var path in primary.Concat(sibling))
        {
            File.Exists(path).Should().BeTrue(
                "'{0}' is inside its OWN database's RetainCount and must not be pruned by the other's run",
                Path.GetFileName(path));
        }
    }

    [Fact]
    public void Retention_still_recognises_and_ages_out_snapshots_from_the_previous_naming_scheme()
    {
        // Compatibility: snapshots already on disk from shipped v0.1.0 are stem-keyed and carry
        // no sequence suffix. They must keep taking part in retention, or a host that upgrades
        // would keep them forever.
        CreateStandaloneWalDatabase();
        Directory.CreateDirectory(DefaultBackupDirectory);

        var legacy = new[] { "20260101T000000000Z", "20260102T000000000Z", "20260103T000000000Z" }
            .Select(stamp => Path.Combine(
                DefaultBackupDirectory,
                Path.GetFileNameWithoutExtension(_dbPath) + SqlitePreMigrationBackup.FileNameMarker
                    + stamp + SqlitePreMigrationBackup.FileExtension))
            .ToList();
        foreach (var path in legacy)
        {
            File.WriteAllText(path, "snapshot written by the v0.1.0 naming scheme");
        }

        var fresh = SqlitePreMigrationBackup.Create(
            _dbPath, new DatabaseBackupSettings { RetainCount = 2 }, logger: null);

        File.Exists(fresh).Should().BeTrue("the newly written snapshot is always the newest");
        File.Exists(legacy[2]).Should().BeTrue(
            "legacy snapshots share one retention window with the new ones, newest first");
        File.Exists(legacy[1]).Should().BeFalse("legacy snapshots must age out, not accumulate forever");
        File.Exists(legacy[0]).Should().BeFalse("legacy snapshots must age out, not accumulate forever");
    }

    [Fact]
    public void Retention_keeps_the_newest_snapshot_after_the_wall_clock_steps_backwards()
    {
        CreateStandaloneWalDatabase();
        Directory.CreateDirectory(DefaultBackupDirectory);

        // Stands in for "the host clock was far ahead when this snapshot was taken, then NTP (or
        // a VM restore) corrected it backwards": an OLDER snapshot — sequence 1 — whose embedded
        // timestamp sorts after anything UtcNow can produce. Ordering retention by the timestamp
        // would call this the newest and delete the real newest first (#1839).
        var clockSkewed = Path.Combine(
            DefaultBackupDirectory,
            Path.GetFileName(_dbPath) + SqlitePreMigrationBackup.FileNameMarker
                + "29991231T235959999Z-000001" + SqlitePreMigrationBackup.FileExtension);
        File.WriteAllText(clockSkewed, "written while the clock was ahead");

        var fresh = SqlitePreMigrationBackup.Create(
            _dbPath, new DatabaseBackupSettings { RetainCount = 1 }, logger: null);

        Path.GetFileName(fresh).Should().Contain(
            "-000002", "the sequence must continue from the highest already on disk, not restart");
        string.CompareOrdinal(Path.GetFileName(fresh), Path.GetFileName(clockSkewed)).Should().BeNegative(
            "the fixture only proves anything if the NEWEST snapshot sorts BEFORE the skewed one by name");

        File.Exists(fresh).Should().BeTrue(
            "the newest snapshot must survive a clock that stepped backwards — it is the one the " +
            "user needs to recover the upgrade that just ran");
        File.Exists(clockSkewed).Should().BeFalse(
            "the older, future-timestamped snapshot is the one retention should have pruned");
    }

    // ── When NOT to back up ──────────────────────────────────────────────────

    [Fact]
    public void No_backup_is_taken_when_there_are_no_pending_migrations()
    {
        using (var first = NewFileContext())
        {
            SerializedMigrator.Migrate(first, new DatabaseBackupSettings());
        }

        // The first run created the file from nothing, so there was nothing to protect.
        ListBackups(DefaultBackupDirectory).Should().BeEmpty(
            "a first run that creates the database has no prior state to back up");

        using (var second = NewFileContext())
        {
            second.Database.GetPendingMigrations().Should().BeEmpty();
            SerializedMigrator.Migrate(second, new DatabaseBackupSettings());
        }

        ListBackups(DefaultBackupDirectory).Should().BeEmpty(
            "an ordinary boot with an up-to-date schema must not copy the database");
    }

    [Fact]
    public void No_backup_is_taken_when_backups_are_disabled()
    {
        var lastMigration = MigrateToOneBeforeLatest();

        using (var context = NewFileContext())
        {
            SerializedMigrator.Migrate(context, new DatabaseBackupSettings { Enabled = false });
        }

        ListBackups(DefaultBackupDirectory).Should().BeEmpty(
            "Database:Backup:Enabled=false must skip the snapshot entirely");
        ReadAppliedMigrationIds(_dbPath).Should().Contain(
            lastMigration, "disabling backups must not block the migration itself");
    }

    // ── Fail closed ──────────────────────────────────────────────────────────

    [Fact]
    public void A_backup_that_cannot_be_written_blocks_the_migration()
    {
        var lastMigration = MigrateToOneBeforeLatest();

        // A regular FILE where the backup directory should be: creating a directory under it is
        // impossible on every supported platform, so this is a portable "the backup cannot be
        // written" condition without needing ACL manipulation.
        var blockingFile = Path.Combine(_root, "blocked");
        File.WriteAllText(blockingFile, "not a directory");
        var settings = new DatabaseBackupSettings { Directory = Path.Combine("blocked", "nested") };

        var logger = new InMemoryLogger<PreMigrationBackupTests>();
        using var context = NewFileContext();

        var act = () => SerializedMigrator.Migrate(context, settings, logger);

        var thrown = act.Should().Throw<PreMigrationBackupException>(
            "an unwritable backup must fail closed — migrating unprotected is the failure mode " +
            "this feature exists to prevent").Which;
        thrown.Message.Should().Contain(_dbPath, "the error must name the database at risk");
        thrown.Message.Should().Contain(
            "was NOT applied", "the error must tell the user the migration did not run");
        thrown.Message.Should().Contain(
            "Database:Backup:", "the error must point at the settings that resolve it");
        thrown.InnerException.Should().NotBeNull("the underlying cause must not be swallowed");

        ReadAppliedMigrationIds(_dbPath).Should().NotContain(
            lastMigration, "the pending migration must NOT have been applied after a failed backup");
    }

    // ── Directory resolution ─────────────────────────────────────────────────

    [Fact]
    public void Backup_directory_defaults_to_a_backups_folder_next_to_the_database()
    {
        SqlitePreMigrationBackup.ResolveBackupDirectory(_dbPath, new DatabaseBackupSettings())
            .Should().Be(DefaultBackupDirectory);
    }

    [Fact]
    public void A_relative_configured_backup_directory_resolves_against_the_database_directory()
    {
        // NOT the process working directory: the API, CLI, and MCP hosts do not share one, and
        // a relative path that moved with the cwd would scatter a user's backups.
        var settings = new DatabaseBackupSettings { Directory = "snapshots" };

        SqlitePreMigrationBackup.ResolveBackupDirectory(_dbPath, settings)
            .Should().Be(Path.Combine(_root, "snapshots"));
    }

    [Fact]
    public void An_absolute_configured_backup_directory_is_used_as_given()
    {
        var absolute = Path.Combine(_root, "elsewhere");
        var settings = new DatabaseBackupSettings { Directory = absolute };

        SqlitePreMigrationBackup.ResolveBackupDirectory(_dbPath, settings)
            .Should().Be(Path.GetFullPath(absolute));
    }

    // ── Fixtures and helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Applies every migration except the last, leaving a real, populated database file with
    /// exactly one migration pending — the state an upgrade actually runs against.
    /// </summary>
    /// <returns>The id of the migration left pending.</returns>
    private string MigrateToOneBeforeLatest()
    {
        using var context = NewFileContext();
        var migrations = context.Database.GetMigrations().ToList();
        migrations.Should().HaveCountGreaterThan(
            1, "the fixture needs at least two migrations to leave one pending");

        context.GetService<IMigrator>().Migrate(migrations[^2]);
        return migrations[^1];
    }

    /// <summary>
    /// Writes a row of pre-migration user data into the database. A dedicated fixture table is
    /// used rather than a real entity table so the assertion "the snapshot contains the data
    /// that existed before the upgrade" cannot break when a migration reshapes an entity — the
    /// point under test is the snapshot, not the schema.
    /// </summary>
    private void SeedPreMigrationRow()
    {
        using var connection = new SqliteConnection(TestSqlite.ConnectionString(_dbPath));
        connection.Open();
        Execute(connection, "PRAGMA busy_timeout=" + BusyTimeoutMs);
        Execute(connection, "CREATE TABLE IF NOT EXISTS BackupFixture (Id INTEGER PRIMARY KEY, Body TEXT NOT NULL)");
        Execute(connection, "INSERT INTO BackupFixture (Body) VALUES ('pre-upgrade user data')");
    }

    /// <summary>
    /// A minimal WAL-mode SQLite file, used by the tests that exercise the snapshot mechanics
    /// themselves and do not need Taskdeck's full migration chain.
    /// </summary>
    private void CreateStandaloneWalDatabase() => CreateStandaloneWalDatabase(_dbPath);

    private static void CreateStandaloneWalDatabase(string dbPath)
    {
        using var connection = new SqliteConnection(TestSqlite.ConnectionString(dbPath));
        connection.Open();
        Execute(connection, "PRAGMA journal_mode=WAL");
        Execute(connection, "CREATE TABLE IF NOT EXISTS Notes (Id INTEGER PRIMARY KEY, Body TEXT NOT NULL)");
    }

    private void InsertNote(string body)
    {
        using var connection = new SqliteConnection(TestSqlite.ConnectionString(_dbPath));
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Notes (Body) VALUES ($body)";
        command.Parameters.AddWithValue("$body", body);
        command.ExecuteNonQuery();
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteScalar();
    }

    private static List<string> ListBackups(string directory) =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*" + SqlitePreMigrationBackup.FileNameMarker + "*")
                .OrderBy(Path.GetFileName, StringComparer.Ordinal)
                .ToList()
            : new List<string>();

    private static List<string> ReadAppliedMigrationIds(string dbPath)
    {
        using var connection = new SqliteConnection(TestSqlite.ConnectionString(dbPath));
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory";
        using var reader = command.ExecuteReader();

        var ids = new List<string>();
        while (reader.Read())
        {
            ids.Add(reader.GetString(0));
        }

        return ids;
    }

    private static long CountRows(string dbPath, string table)
    {
        using var connection = new SqliteConnection(TestSqlite.ConnectionString(dbPath));
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    public void Dispose()
    {
        try
        {
            // Pooling=False everywhere here (TestSqlite, #1609), so every handle is already
            // released and the whole isolated tree can go.
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup: a leaked temp directory must never fail an otherwise green run.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
