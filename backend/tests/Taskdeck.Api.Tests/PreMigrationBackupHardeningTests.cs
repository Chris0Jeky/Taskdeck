using System.Globalization;
using System.Numerics;
using System.Reflection;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Taskdeck.Application.Services;
using Taskdeck.Infrastructure.Persistence;
using Taskdeck.Tests.Support;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Regression coverage for the post-#1849 pre-migration backup hardening residuals (#1856).
/// These cases stay separate from the end-to-end migration fixture so the filename and cleanup
/// contracts remain small, direct, and inexpensive to diagnose.
/// </summary>
public sealed class PreMigrationBackupHardeningTests : IDisposable
{
    private readonly string _root;
    private readonly string _dbPath;
    private readonly string _backupDirectory;

    public PreMigrationBackupHardeningTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"taskdeck-premigration-hardening-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "taskdeck.db");
        _backupDirectory = Path.Combine(_root, SqlitePreMigrationBackup.DefaultDirectoryName);
    }

    [Fact]
    public void Backup_prunes_only_stale_strictly_named_temporary_snapshots()
    {
        CreateStandaloneWalDatabase();
        Directory.CreateDirectory(_backupDirectory);

        var staleTemporary = ManagedTemporarySnapshot("20260101T000000000Z", "000001");
        var staleWal = staleTemporary + "-wal";
        var staleShm = staleTemporary + "-shm";
        var recentTemporary = ManagedTemporarySnapshot("20260102T000000000Z", "000002");
        var oldTemporaryWithRecentWal = ManagedTemporarySnapshot("20260103T000000000Z", "000003");
        var recentWal = oldTemporaryWithRecentWal + "-wal";
        var unrelatedTemporary = Path.Combine(_backupDirectory, "manual-copy.db.tmp");

        foreach (var path in new[]
                 {
                     staleTemporary,
                     staleWal,
                     staleShm,
                     recentTemporary,
                     oldTemporaryWithRecentWal,
                     recentWal,
                     unrelatedTemporary,
                 })
        {
            File.WriteAllText(path, "orphan fixture");
        }

        var staleAt = DateTime.UtcNow.Subtract(TimeSpan.FromDays(2));
        foreach (var path in new[]
                 {
                     staleTemporary,
                     staleWal,
                     staleShm,
                     oldTemporaryWithRecentWal,
                     unrelatedTemporary,
                 })
        {
            File.SetLastWriteTimeUtc(path, staleAt);
        }

        var recentAt = DateTime.UtcNow;
        File.SetLastWriteTimeUtc(recentTemporary, recentAt);
        File.SetLastWriteTimeUtc(recentWal, recentAt);

        var created = SqlitePreMigrationBackup.Create(
            _dbPath,
            new DatabaseBackupSettings { RetainCount = 5 },
            logger: null);

        File.Exists(created).Should().BeTrue("the hardening cleanup must not interfere with the protective snapshot");
        File.Exists(staleTemporary).Should().BeFalse("a crash-old managed staging file is never a usable backup");
        File.Exists(staleWal).Should().BeFalse("the stale staging file's WAL sidecar belongs to the same orphan");
        File.Exists(staleShm).Should().BeFalse("the stale staging file's SHM sidecar belongs to the same orphan");
        File.Exists(recentTemporary).Should().BeTrue("a young staging file may still belong to another live process");
        File.Exists(oldTemporaryWithRecentWal).Should().BeTrue(
            "a recently active SQLite sidecar means the staging set may still belong to a live process");
        File.Exists(recentWal).Should().BeTrue("cleanup must preserve the complete live staging set");
        File.Exists(unrelatedTemporary).Should().BeTrue("cleanup must only touch this helper's strict filename contract");
    }

    [Fact]
    public void Backup_discovers_sidecar_only_orphans_and_preserves_recent_or_unrelated_sidecars()
    {
        CreateStandaloneWalDatabase();
        Directory.CreateDirectory(_backupDirectory);

        var staleWalOnly = ManagedTemporarySnapshot("20260104T000000000Z", "000004") + "-wal";
        var staleShmOnly = ManagedTemporarySnapshot("20260105T000000000Z", "000005") + "-shm";
        var recentWalOnly = ManagedTemporarySnapshot("20260106T000000000Z", "000006") + "-wal";
        var unrelatedWalOnly = Path.Combine(_backupDirectory, "manual-copy.db.tmp-wal");

        foreach (var path in new[] { staleWalOnly, staleShmOnly, recentWalOnly, unrelatedWalOnly })
        {
            File.WriteAllText(path, "sidecar-only orphan fixture");
        }

        var staleAt = DateTime.UtcNow.Subtract(TimeSpan.FromDays(2));
        File.SetLastWriteTimeUtc(staleWalOnly, staleAt);
        File.SetLastWriteTimeUtc(staleShmOnly, staleAt);
        File.SetLastWriteTimeUtc(unrelatedWalOnly, staleAt);
        File.SetLastWriteTimeUtc(recentWalOnly, DateTime.UtcNow);

        SqlitePreMigrationBackup.Create(
            _dbPath,
            new DatabaseBackupSettings { RetainCount = 5 },
            logger: null);

        File.Exists(staleWalOnly).Should().BeFalse(
            "cleanup must retry a stale WAL sidecar even when an earlier pass already removed the main staging file");
        File.Exists(staleShmOnly).Should().BeFalse(
            "cleanup must retry a stale SHM sidecar even when an earlier pass already removed the main staging file");
        File.Exists(recentWalOnly).Should().BeTrue(
            "a recent sidecar-only set may still belong to an active process and must not be reclaimed");
        File.Exists(unrelatedWalOnly).Should().BeTrue(
            "sidecar discovery must still enforce the helper's strict managed filename contract");
    }

    [Fact]
    public void Backup_preserves_stale_noncanonical_staging_names()
    {
        CreateStandaloneWalDatabase();
        Directory.CreateDirectory(_backupDirectory);

        var nonAsciiTimestamp = ManagedTemporarySnapshot("2026010١T000000000Z", "000007");
        var impossibleTimestamp = ManagedTemporarySnapshot("20261301T000000000Z", "000008");
        var nonCanonicalSequence = ManagedTemporarySnapshot("20260107T000000000Z", "0000009");
        var zeroSequence = ManagedTemporarySnapshot("20260108T000000000Z", "000000");
        var candidates = new[]
        {
            nonAsciiTimestamp,
            impossibleTimestamp,
            nonCanonicalSequence,
            zeroSequence,
        };

        foreach (var path in candidates)
        {
            File.WriteAllText(path, "non-canonical staging fixture");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.Subtract(TimeSpan.FromDays(2)));
        }

        SqlitePreMigrationBackup.Create(
            _dbPath,
            new DatabaseBackupSettings { RetainCount = 5 },
            logger: null);

        foreach (var path in candidates)
        {
            File.Exists(path).Should().BeTrue(
                "cleanup may delete only a canonical filename that this helper can actually emit");
        }
    }

    [Fact]
    public void Retention_orders_and_prunes_sequences_larger_than_long_max_value()
    {
        CreateStandaloneWalDatabase();
        Directory.CreateDirectory(_backupDirectory);

        var oversizedDigits = new string('9', 40);
        var oversizedSnapshot = Path.Combine(
            _backupDirectory,
            Path.GetFileName(_dbPath)
                + SqlitePreMigrationBackup.FileNameMarker
                + "20260101T000000000Z-"
                + oversizedDigits
                + SqlitePreMigrationBackup.FileExtension);
        File.WriteAllText(oversizedSnapshot, "managed snapshot with an arbitrary-precision sequence");

        var created = SqlitePreMigrationBackup.Create(
            _dbPath,
            new DatabaseBackupSettings { RetainCount = 1 },
            logger: null);

        var expectedNext = (BigInteger.Parse(oversizedDigits, CultureInfo.InvariantCulture) + BigInteger.One)
            .ToString("D6", CultureInfo.InvariantCulture);

        Path.GetFileName(created).Should().EndWith(
            "-" + expectedNext + SqlitePreMigrationBackup.FileExtension,
            "sequence allocation must continue past every managed snapshot already on disk");
        File.Exists(oversizedSnapshot).Should().BeFalse(
            "an oversized but valid managed sequence must participate in retention instead of lingering forever");
        File.Exists(created).Should().BeTrue("retention of one must keep the newly allocated highest sequence");
    }

    [Fact]
    public void Snapshot_filename_identity_is_ordinal()
    {
        var field = typeof(SqlitePreMigrationBackup).GetField(
            "SnapshotFileNameComparer",
            BindingFlags.NonPublic | BindingFlags.Static);

        field.Should().NotBeNull(
            "the comparer policy must be explicit because case-insensitive dedupe can let an invalid case variant hide a valid Linux snapshot");

        var comparer = field!.GetValue(null).Should().BeAssignableTo<StringComparer>().Which;
        comparer.Equals(
                "taskdeck.db-pre-migration-20260101T000000000Z-000001.db",
                "taskdeck.db-pre-migration-20260101t000000000z-000001.db")
            .Should().BeFalse(
                "case-distinct names are separate entries on a case-sensitive filesystem and cannot coexist on a case-insensitive one");
    }

    private string ManagedTemporarySnapshot(string timestamp, string sequence) =>
        Path.Combine(
            _backupDirectory,
            Path.GetFileName(_dbPath)
                + SqlitePreMigrationBackup.FileNameMarker
                + timestamp
                + "-"
                + sequence
                + SqlitePreMigrationBackup.FileExtension
                + ".tmp");

    private void CreateStandaloneWalDatabase()
    {
        using var connection = new SqliteConnection(TestSqlite.ConnectionString(_dbPath));
        connection.Open();
        Execute(connection, "PRAGMA journal_mode=WAL");
        Execute(connection, "CREATE TABLE IF NOT EXISTS Notes (Id INTEGER PRIMARY KEY, Body TEXT NOT NULL)");
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteScalar();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A failed assertion is the useful signal; best-effort temp cleanup must not hide it.
        }
        catch (UnauthorizedAccessException)
        {
            // Windows can briefly retain SQLite handles after a failed test. The OS temp sweep owns it.
        }
    }
}