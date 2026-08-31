using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Taskdeck.Cli.Commands;
using Taskdeck.Domain.Connectors;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Taskdeck.Infrastructure.Services;
using Xunit;

namespace Taskdeck.Cli.Tests;

public class DatabaseRecoveryCommandTests
{
    private const string BackupKey = "AgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgI=";
    private const string WrongBackupKey = "AwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwM=";
    private const string ConnectorKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    [Theory]
    [InlineData("--backup")]
    [InlineData("--restore")]
    [InlineData("--BACKUP")]
    [InlineData("--RESTORE")]
    public void IsRequest_RecognizesRecoveryCommands(string request)
    {
        DatabaseRecoveryCommand.IsRequest(new[] { request }).Should().BeTrue();
    }

    [Fact]
    public async Task BackupAndRestore_RoundTripDataIntegrityAndConnectorVerification()
    {
        await using var harness = new CliTestHarness("cli-recovery-roundtrip");
        await harness.RunAsync("boards create RecoveryBoard --json");
        await SeedCredentialAsync(harness.DatabasePath, ConnectorKey, "recovery-secret");
        var backupKeyPath = await WriteKeyFileAsync(harness.DataDirectory, "backup.key", BackupKey);
        var connectorKeyPath = await WriteKeyFileAsync(harness.DataDirectory, "connector.key", ConnectorKey);
        var outputDirectory = Path.Combine(harness.DataDirectory, "archives");
        Directory.CreateDirectory(outputDirectory);

        var backup = await harness.RunAsync(
            $"--backup --database \"{harness.DatabasePath}\" --output \"{outputDirectory}\" --key-file \"{backupKeyPath}\"");

        backup.ExitCode.Should().Be(ExitCodes.Success, backup.StdErr);
        backup.StdErr.Should().BeEmpty();
        backup.StdOut.Should().Contain("integrity=ok");
        backup.StdOut.Should().NotContain("recovery-secret");
        var archivePath = ReadOutputValue(backup.StdOut, "archive");
        var schemaVersion = ReadOutputValue(backup.StdOut, "schema");
        File.Exists(archivePath).Should().BeTrue();
        Path.GetExtension(archivePath).Should().Be(".tdbk");
        Path.GetFileName(archivePath).Should()
            .StartWith("taskdeck-backup-")
            .And.Contain($"-schema-{schemaVersion}-")
            .And.EndWith(".tdbk");
        Encoding.ASCII.GetString(await File.ReadAllBytesAsync(archivePath))
            .Should().NotContain("SQLite format 3");
        AssertNoPlaintextStaging(outputDirectory);

        var restoredPath = Path.Combine(harness.DataDirectory, "restored.db");
        await File.WriteAllTextAsync($"{restoredPath}-wal", "stale-wal");
        await File.WriteAllTextAsync($"{restoredPath}-shm", "stale-shm");
        await File.WriteAllTextAsync($"{restoredPath}-journal", "stale-journal");
        var restore = await harness.RunAsync(
            $"--restore --archive \"{archivePath}\" --database \"{restoredPath}\" " +
            $"--key-file \"{backupKeyPath}\" --connector-key-file \"{connectorKeyPath}\"");

        restore.ExitCode.Should().Be(ExitCodes.Success, restore.StdErr);
        restore.StdErr.Should().BeEmpty();
        restore.StdOut.Should().Contain("integrity=ok");
        restore.StdOut.Should().Contain("connectors ok=1 failed=0");
        restore.StdOut.Should().NotContain("recovery-secret");
        (await CountRowsAsync(restoredPath, "Boards")).Should().Be(1);
        (await CountRowsAsync(restoredPath, "ConnectorCredentials")).Should().Be(1);
        AssertNoJournalFiles(restoredPath);
        AssertNoRestoreStaging(harness.DataDirectory);
    }

    [Fact]
    public async Task Restore_ExistingTarget_CreatesEncryptedSafetyArchiveBeforeReplacement()
    {
        await using var harness = new CliTestHarness("cli-recovery-existing-target");
        var existingTarget = Path.Combine(harness.DataDirectory, "existing-target.db");
        File.Copy(harness.DatabasePath, existingTarget);
        var create = await harness.RunAsync("boards create ReplacementBoard --json");
        create.ExitCode.Should().Be(ExitCodes.Success, create.StdErr);
        var backupKeyPath = await WriteKeyFileAsync(harness.DataDirectory, "backup.key", BackupKey);
        var connectorKeyPath = await WriteKeyFileAsync(harness.DataDirectory, "connector.key", ConnectorKey);
        var outputDirectory = Path.Combine(harness.DataDirectory, "archives");
        Directory.CreateDirectory(outputDirectory);
        var backup = await harness.RunAsync(
            $"--backup --database \"{harness.DatabasePath}\" --output \"{outputDirectory}\" --key-file \"{backupKeyPath}\"");
        var archivePath = ReadOutputValue(backup.StdOut, "archive");

        var restore = await harness.RunAsync(
            $"--restore --archive \"{archivePath}\" --database \"{existingTarget}\" " +
            $"--key-file \"{backupKeyPath}\" --connector-key-file \"{connectorKeyPath}\"");

        restore.ExitCode.Should().Be(ExitCodes.Success, restore.StdErr);
        var safetyArchive = ReadOutputValue(restore.StdOut, "safetyArchive");
        File.Exists(safetyArchive).Should().BeTrue();
        Path.GetExtension(safetyArchive).Should().Be(".tdbk");
        (await CountRowsAsync(existingTarget, "Boards")).Should().Be(1);

        var safetyRestored = Path.Combine(harness.DataDirectory, "safety-restored.db");
        var restoreSafety = await harness.RunAsync(
            $"--restore --archive \"{safetyArchive}\" --database \"{safetyRestored}\" " +
            $"--key-file \"{backupKeyPath}\" --connector-key-file \"{connectorKeyPath}\"");
        restoreSafety.ExitCode.Should().Be(ExitCodes.Success, restoreSafety.StdErr);
        (await CountRowsAsync(safetyRestored, "Boards")).Should().Be(0);
        AssertNoJournalFiles(existingTarget);
    }

    [Fact]
    public async Task Restore_PostPromotionFailure_RestoresExistingTargetAndRetainsSafetyArchive()
    {
        await using var harness = new CliTestHarness("cli-recovery-post-promotion-rollback");
        var existingTarget = Path.Combine(harness.DataDirectory, "existing-target.db");
        File.Copy(harness.DatabasePath, existingTarget);
        var create = await harness.RunAsync("boards create ReplacementBoard --json");
        create.ExitCode.Should().Be(ExitCodes.Success, create.StdErr);
        var outputDirectory = Path.Combine(harness.DataDirectory, "archives");
        Directory.CreateDirectory(outputDirectory);
        var key = Convert.FromBase64String(BackupKey);

        try
        {
            var backup = await DatabaseRecoveryCommand.CreateBackupAsync(
                harness.DatabasePath,
                outputDirectory,
                key);

            Func<Task> restore = async () => await DatabaseRecoveryCommand.RestoreAsync(
                backup.ArchivePath,
                existingTarget,
                key,
                ConnectorKey,
                postPromotionProbe: static (_, _) =>
                    Task.FromException(new IOException("injected post-promotion failure")));

            await restore.Should().ThrowAsync<IOException>();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        (await CountRowsAsync(existingTarget, "Boards")).Should().Be(0);
        AssertNoJournalFiles(existingTarget);
        AssertNoRestoreStaging(harness.DataDirectory);
        Directory.EnumerateFiles(
                harness.DataDirectory,
                "taskdeck-pre-restore-*.tdbk",
                SearchOption.TopDirectoryOnly)
            .Should().ContainSingle();
        Directory.EnumerateFiles(
                harness.DataDirectory,
                ".*.rollback-*.db",
                SearchOption.TopDirectoryOnly)
            .Should().BeEmpty();
    }

    [Fact]
    public async Task Backup_MissingKey_FailsWithoutCreatingAnArchive()
    {
        await using var harness = new CliTestHarness("cli-recovery-missing-key");
        var outputDirectory = Path.Combine(harness.DataDirectory, "archives");
        Directory.CreateDirectory(outputDirectory);

        var result = await harness.RunAsync(
            $"--backup --database \"{harness.DatabasePath}\" --output \"{outputDirectory}\"",
            new Dictionary<string, string?>
            {
                ["TASKDECK_BACKUP_KEY"] = null,
                ["TASKDECK_BACKUP_KEY_FILE"] = null
            });

        result.ExitCode.Should().Be(ExitCodes.Failure);
        result.StdOut.Should().BeEmpty();
        result.StdErr.Should().Contain("Backup encryption key was not supplied");
        Directory.EnumerateFileSystemEntries(outputDirectory).Should().BeEmpty();
    }

    [Fact]
    public async Task Restore_MissingBackupKey_FailsWithoutCreatingTheTarget()
    {
        await using var harness = new CliTestHarness("cli-recovery-missing-restore-backup-key");
        var backupKeyPath = await WriteKeyFileAsync(harness.DataDirectory, "backup.key", BackupKey);
        var connectorKeyPath = await WriteKeyFileAsync(harness.DataDirectory, "connector.key", ConnectorKey);
        var outputDirectory = Path.Combine(harness.DataDirectory, "archives");
        Directory.CreateDirectory(outputDirectory);
        var backup = await harness.RunAsync(
            $"--backup --database \"{harness.DatabasePath}\" --output \"{outputDirectory}\" --key-file \"{backupKeyPath}\"");
        var archivePath = ReadOutputValue(backup.StdOut, "archive");
        var targetPath = Path.Combine(harness.DataDirectory, "missing-backup-key-target.db");

        var restore = await harness.RunAsync(
            $"--restore --archive \"{archivePath}\" --database \"{targetPath}\" " +
            $"--connector-key-file \"{connectorKeyPath}\"",
            new Dictionary<string, string?>
            {
                ["TASKDECK_BACKUP_KEY"] = null,
                ["TASKDECK_BACKUP_KEY_FILE"] = null
            });

        restore.ExitCode.Should().Be(ExitCodes.Failure);
        restore.StdOut.Should().BeEmpty();
        restore.StdErr.Should().Contain("Backup encryption key was not supplied");
        File.Exists(targetPath).Should().BeFalse();
        AssertNoRestoreStaging(harness.DataDirectory);
    }

    [Fact]
    public async Task Restore_MissingConnectorKey_FailsWithoutCreatingTheTarget()
    {
        await using var harness = new CliTestHarness("cli-recovery-missing-restore-connector-key");
        var backupKeyPath = await WriteKeyFileAsync(harness.DataDirectory, "backup.key", BackupKey);
        var outputDirectory = Path.Combine(harness.DataDirectory, "archives");
        Directory.CreateDirectory(outputDirectory);
        var backup = await harness.RunAsync(
            $"--backup --database \"{harness.DatabasePath}\" --output \"{outputDirectory}\" --key-file \"{backupKeyPath}\"");
        var archivePath = ReadOutputValue(backup.StdOut, "archive");
        var targetPath = Path.Combine(harness.DataDirectory, "missing-connector-key-target.db");

        var restore = await harness.RunAsync(
            $"--restore --archive \"{archivePath}\" --database \"{targetPath}\" --key-file \"{backupKeyPath}\"",
            new Dictionary<string, string?>
            {
                ["Connectors__EncryptionKey"] = null,
                ["TASKDECK_CONNECTORS__ENCRYPTIONKEY"] = null,
                ["TASKDECK_CONNECTOR_KEY_FILE"] = null
            });

        restore.ExitCode.Should().Be(ExitCodes.Failure);
        restore.StdOut.Should().BeEmpty();
        restore.StdErr.Should().Contain("Connector encryption key was not supplied");
        File.Exists(targetPath).Should().BeFalse();
        AssertNoRestoreStaging(harness.DataDirectory);
    }

    [Fact]
    public async Task InvalidRecoveryOptions_ReturnStableUsageExitCode()
    {
        await using var harness = new CliTestHarness("cli-recovery-usage");

        var result = await harness.RunAsync("--backup --unknown value");

        result.ExitCode.Should().Be(ExitCodes.Usage);
        result.StdOut.Should().BeEmpty();
        result.StdErr.Should().Contain("Usage: taskdeck-backup");
    }

    [Fact]
    public async Task Restore_WrongKey_FailsWithoutCreatingTheTargetOrLeakingDetails()
    {
        await using var harness = new CliTestHarness("cli-recovery-wrong-key");
        var backupKeyPath = await WriteKeyFileAsync(harness.DataDirectory, "backup.key", BackupKey);
        var wrongKeyPath = await WriteKeyFileAsync(harness.DataDirectory, "wrong.key", WrongBackupKey);
        var connectorKeyPath = await WriteKeyFileAsync(harness.DataDirectory, "connector.key", ConnectorKey);
        var outputDirectory = Path.Combine(harness.DataDirectory, "archives");
        Directory.CreateDirectory(outputDirectory);
        var backup = await harness.RunAsync(
            $"--backup --database \"{harness.DatabasePath}\" --output \"{outputDirectory}\" --key-file \"{backupKeyPath}\"");
        var archivePath = ReadOutputValue(backup.StdOut, "archive");
        var targetPath = Path.Combine(harness.DataDirectory, "wrong-key-target.db");

        var restore = await harness.RunAsync(
            $"--restore --archive \"{archivePath}\" --database \"{targetPath}\" " +
            $"--key-file \"{wrongKeyPath}\" --connector-key-file \"{connectorKeyPath}\"");

        restore.ExitCode.Should().Be(ExitCodes.Failure);
        restore.StdOut.Should().BeEmpty();
        restore.StdErr.Should().Contain("Restore could not be completed");
        restore.StdErr.Should().NotContain(archivePath);
        File.Exists(targetPath).Should().BeFalse();
        AssertNoRestoreStaging(harness.DataDirectory);
    }

    [Fact]
    public async Task Restore_TamperedArchive_FailsWithoutPlaintextResidue()
    {
        await using var harness = new CliTestHarness("cli-recovery-tamper");
        var backupKeyPath = await WriteKeyFileAsync(harness.DataDirectory, "backup.key", BackupKey);
        var connectorKeyPath = await WriteKeyFileAsync(harness.DataDirectory, "connector.key", ConnectorKey);
        var outputDirectory = Path.Combine(harness.DataDirectory, "archives");
        Directory.CreateDirectory(outputDirectory);
        var backup = await harness.RunAsync(
            $"--backup --database \"{harness.DatabasePath}\" --output \"{outputDirectory}\" --key-file \"{backupKeyPath}\"");
        var archivePath = ReadOutputValue(backup.StdOut, "archive");
        var bytes = await File.ReadAllBytesAsync(archivePath);
        bytes[^20] ^= 0x5a;
        await File.WriteAllBytesAsync(archivePath, bytes);
        var targetPath = Path.Combine(harness.DataDirectory, "tampered-target.db");

        var restore = await harness.RunAsync(
            $"--restore --archive \"{archivePath}\" --database \"{targetPath}\" " +
            $"--key-file \"{backupKeyPath}\" --connector-key-file \"{connectorKeyPath}\"");

        restore.ExitCode.Should().Be(ExitCodes.Failure);
        File.Exists(targetPath).Should().BeFalse();
        AssertNoRestoreStaging(harness.DataDirectory);
    }

    [Fact]
    public async Task Restore_WrongConnectorKey_FailsBeforeCreatingTheTarget()
    {
        await using var harness = new CliTestHarness("cli-recovery-wrong-connector-key");
        await SeedCredentialAsync(harness.DatabasePath, ConnectorKey, "connector-secret");
        var backupKeyPath = await WriteKeyFileAsync(harness.DataDirectory, "backup.key", BackupKey);
        var wrongConnectorKeyPath = await WriteKeyFileAsync(
            harness.DataDirectory,
            "wrong-connector.key",
            WrongBackupKey);
        var outputDirectory = Path.Combine(harness.DataDirectory, "archives");
        Directory.CreateDirectory(outputDirectory);
        var backup = await harness.RunAsync(
            $"--backup --database \"{harness.DatabasePath}\" --output \"{outputDirectory}\" --key-file \"{backupKeyPath}\"");
        var archivePath = ReadOutputValue(backup.StdOut, "archive");
        var targetPath = Path.Combine(harness.DataDirectory, "wrong-connector-target.db");

        var restore = await harness.RunAsync(
            $"--restore --archive \"{archivePath}\" --database \"{targetPath}\" " +
            $"--key-file \"{backupKeyPath}\" --connector-key-file \"{wrongConnectorKeyPath}\"");

        restore.ExitCode.Should().Be(ExitCodes.Failure);
        restore.StdErr.Should().Contain("Restored connector credentials could not be verified");
        File.Exists(targetPath).Should().BeFalse();
        AssertNoRestoreStaging(harness.DataDirectory);
    }

    [Fact]
    public async Task Backup_CapturesCommittedWalDataIntoAStandaloneArchive()
    {
        await using var harness = new CliTestHarness("cli-recovery-wal");
        await using var writer = await OpenWalWriterAsync(harness.DatabasePath);
        var backupKeyPath = await WriteKeyFileAsync(harness.DataDirectory, "backup.key", BackupKey);
        var connectorKeyPath = await WriteKeyFileAsync(harness.DataDirectory, "connector.key", ConnectorKey);
        var outputDirectory = Path.Combine(harness.DataDirectory, "archives");
        Directory.CreateDirectory(outputDirectory);

        var backup = await harness.RunAsync(
            $"--backup --database \"{harness.DatabasePath}\" --output \"{outputDirectory}\" --key-file \"{backupKeyPath}\"");
        var archivePath = ReadOutputValue(backup.StdOut, "archive");
        var restoredPath = Path.Combine(harness.DataDirectory, "wal-restored.db");
        var restore = await harness.RunAsync(
            $"--restore --archive \"{archivePath}\" --database \"{restoredPath}\" " +
            $"--key-file \"{backupKeyPath}\" --connector-key-file \"{connectorKeyPath}\"");

        restore.ExitCode.Should().Be(ExitCodes.Success, restore.StdErr);
        (await ReadScalarAsync(restoredPath, "SELECT Value FROM RecoveryBackupSentinel LIMIT 1;"))
            .Should().Be("committed-in-wal");
        AssertNoJournalFiles(restoredPath);
    }

    private static string ReadOutputValue(string output, string key)
    {
        var line = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Single(candidate => candidate.StartsWith($"{key}=", StringComparison.Ordinal));
        return line[(key.Length + 1)..];
    }

    private static async Task<string> WriteKeyFileAsync(string directory, string name, string value)
    {
        var path = Path.Combine(directory, name);
        await File.WriteAllTextAsync(path, value);
        return path;
    }

    private static async Task SeedCredentialAsync(string databasePath, string key, string plaintext)
    {
        var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString())
            .Options;
        var encryption = new AesCredentialEncryptionService(key);
        await using var context = new TaskdeckDbContext(options);
        var user = new User("recovery-user", "recovery-user@example.test", "test-password-hash");
        var connector = new IntegrationConnector(
            "Recovery connector",
            ConnectorType.Custom,
            ConnectorDirection.Inbound,
            user.Id);
        context.Users.Add(user);
        context.IntegrationConnectors.Add(connector);
        await context.SaveChangesAsync();
        context.ConnectorCredentials.Add(new ConnectorCredential(
            connector.Id,
            user.Id,
            ConnectorAuthMethod.ApiKey,
            "Recovery credential",
            encryption.Encrypt(plaintext)));
        await context.SaveChangesAsync();
        await context.Database.CloseConnectionAsync();
        await NormalizeJournalModeAsync(databasePath);
    }

    private static async Task<SqliteConnection> OpenWalWriterAsync(string databasePath)
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString());
        await connection.OpenAsync();
        await using (var mode = connection.CreateCommand())
        {
            mode.CommandText = "PRAGMA journal_mode=WAL;";
            await mode.ExecuteScalarAsync();
        }
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "CREATE TABLE IF NOT EXISTS RecoveryBackupSentinel (Value TEXT NOT NULL);" +
                "DELETE FROM RecoveryBackupSentinel;" +
                "INSERT INTO RecoveryBackupSentinel (Value) VALUES ('committed-in-wal');";
            await command.ExecuteNonQueryAsync();
        }
        File.Exists($"{databasePath}-wal").Should().BeTrue();
        return connection;
    }

    private static async Task NormalizeJournalModeAsync(string databasePath)
    {
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString());
        await connection.OpenAsync();
        await using (var checkpoint = connection.CreateCommand())
        {
            checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            await checkpoint.ExecuteScalarAsync();
        }
        await using (var mode = connection.CreateCommand())
        {
            mode.CommandText = "PRAGMA journal_mode=DELETE;";
            await mode.ExecuteScalarAsync();
        }
    }

    private static async Task<long> CountRowsAsync(string databasePath, string table)
    {
        var value = await ReadScalarAsync(databasePath, $"SELECT COUNT(*) FROM \"{table}\";");
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<object?> ReadScalarAsync(string databasePath, string sql)
    {
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    private static void AssertNoJournalFiles(string path)
    {
        File.Exists($"{path}-wal").Should().BeFalse();
        File.Exists($"{path}-shm").Should().BeFalse();
        File.Exists($"{path}-journal").Should().BeFalse();
    }

    private static void AssertNoPlaintextStaging(string directory) =>
        Directory.EnumerateFiles(directory, "*.db", SearchOption.TopDirectoryOnly)
            .Should().BeEmpty();

    private static void AssertNoRestoreStaging(string directory) =>
        Directory.EnumerateFiles(directory, "*.restore-*", SearchOption.TopDirectoryOnly)
            .Should().BeEmpty();
}
