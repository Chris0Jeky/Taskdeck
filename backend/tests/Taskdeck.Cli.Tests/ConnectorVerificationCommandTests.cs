using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Connectors;
using Taskdeck.Cli;
using Taskdeck.Cli.Commands;
using Taskdeck.Domain.Connectors;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Taskdeck.Infrastructure.Services;
using Xunit;

namespace Taskdeck.Cli.Tests;

public class ConnectorVerificationCommandTests
{
    private const string CorrectKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    private const string WrongKey = "AQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQE=";

    [Theory]
    [InlineData("--verify-connectors")]
    [InlineData("--VERIFY-CONNECTORS")]
    public void IsRequest_RecognizesTheLeadingVerificationFlag(string request)
    {
        ConnectorVerificationCommand.IsRequest(new[] { request, "--database", "taskdeck.db" })
            .Should().BeTrue();
    }

    [Fact]
    public void IsRequest_RejectsAFlagAfterAnotherCommand()
    {
        ConnectorVerificationCommand.IsRequest(new[] { "boards", "--verify-connectors" })
            .Should().BeFalse();
    }

    [Fact]
    public void CreateReadOnlyConnectionString_UsesAnImmutableUri()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "connector verifier #1?.db");

        var connectionString = ConnectorVerificationCommand.CreateReadOnlyConnectionString(databasePath);

        var builder = new SqliteConnectionStringBuilder(connectionString);
        builder.Mode.Should().Be(SqliteOpenMode.ReadOnly);
        builder.Cache.Should().Be(SqliteCacheMode.Private);
        builder.Pooling.Should().BeFalse();
        builder.DataSource.Should().StartWith("file:");
        builder.DataSource.Should().Contain("immutable=1");
        builder.DataSource.Should().Contain("%23");
        builder.DataSource.Should().Contain("%3F");
    }

    [Fact]
    public void VerifyCiphertexts_AttemptsEveryCredentialAndCountsFailures()
    {
        var encryption = new RecordingEncryptionService(value =>
            value == "bad"
                ? throw new CryptographicException("test-only failure")
                : "plaintext");

        var verifier = new ConnectorCredentialVerifier(encryption);
        foreach (var ciphertext in new[] { "first", "bad", "last" })
        {
            verifier.Verify(ciphertext);
        }

        verifier.Counts.Ok.Should().Be(2);
        verifier.Counts.Failed.Should().Be(1);
        encryption.Attempts.Should().Equal("first", "bad", "last");
    }

    [Fact]
    public void VerifyCiphertexts_DoesNotMaskUnexpectedFailures()
    {
        var encryption = new RecordingEncryptionService(_ =>
            throw new InvalidOperationException("test-only unexpected failure"));

        var verifier = new ConnectorCredentialVerifier(encryption);

        var action = () => verifier.Verify("credential");

        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task Execute_CorrectKey_VerifiesEveryCredentialWithoutMutatingDatabase()
    {
        await using var harness = new CliTestHarness("cli-verify-connectors-correct");
        var secrets = new[] { "secret-alpha", "secret-beta" };
        var ciphertexts = await SeedCredentialsAsync(harness.DatabasePath, CorrectKey, secrets);
        var keyPath = await WriteKeyFileAsync(harness.DataDirectory, CorrectKey);
        var beforeHash = ComputeSha256(harness.DatabasePath);
        AssertNoJournalFiles(harness.DatabasePath);

        var result = await harness.RunAsync(
            $"--verify-connectors --database \"{harness.DatabasePath}\" --key-file \"{keyPath}\"");

        result.ExitCode.Should().Be(ExitCodes.Success, result.StdErr);
        result.StdOut.Should().Be("ok=2 failed=0");
        result.StdErr.Should().BeEmpty();
        result.StdOut.Should().NotContainAny(secrets);
        result.StdErr.Should().NotContainAny(secrets);
        foreach (var ciphertext in ciphertexts)
        {
            result.StdOut.Should().NotContain(ciphertext);
            result.StdErr.Should().NotContain(ciphertext);
        }

        ComputeSha256(harness.DatabasePath).Should().Be(beforeHash);
        AssertNoJournalFiles(harness.DatabasePath);
        harness.LastStartupTraceSnapshot.Should().NotBeNull();
        harness.LastStartupTraceSnapshot!.LastPhase.Should().Be(CliStartupTrace.ManagedEntryPhase);
    }

    [Fact]
    public async Task Execute_WrongKey_FailsEveryCredentialWithoutReportingAnEmptyDatabase()
    {
        await using var harness = new CliTestHarness("cli-verify-connectors-wrong");
        await SeedCredentialsAsync(harness.DatabasePath, CorrectKey, "secret-one", "secret-two");
        var keyPath = await WriteKeyFileAsync(harness.DataDirectory, WrongKey);

        var result = await harness.RunAsync(
            $"--verify-connectors --database \"{harness.DatabasePath}\" --key-file \"{keyPath}\"");

        result.ExitCode.Should().Be(ExitCodes.Failure);
        result.StdOut.Should().Be("ok=0 failed=2");
        result.StdOut.Should().NotContain("Nothing to verify.");
        result.StdErr.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_ConfiguredEnvironmentKey_VerifiesWithoutAKeyFile()
    {
        await using var harness = new CliTestHarness("cli-verify-connectors-environment-key");
        await SeedCredentialsAsync(harness.DatabasePath, CorrectKey, "environment-secret");

        var result = await harness.RunAsync(
            $"--verify-connectors --database \"{harness.DatabasePath}\"");

        result.ExitCode.Should().Be(ExitCodes.Success, result.StdErr);
        result.StdOut.Should().Be("ok=1 failed=0");
        result.StdErr.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_WalHeaderWithoutSidecars_VerifiesWithoutCreatingSidecars()
    {
        await using var harness = new CliTestHarness("cli-verify-connectors-wal-header");
        await SeedCredentialsAsync(harness.DatabasePath, CorrectKey, "wal-secret");
        await PrepareStandaloneWalHeaderAsync(harness.DatabasePath);
        var keyPath = await WriteKeyFileAsync(harness.DataDirectory, CorrectKey);
        var beforeHash = ComputeSha256(harness.DatabasePath);
        AssertNoJournalFiles(harness.DatabasePath);

        var result = await harness.RunAsync(
            $"--verify-connectors --database \"{harness.DatabasePath}\" --key-file \"{keyPath}\"");

        result.ExitCode.Should().Be(ExitCodes.Success, result.StdErr);
        result.StdOut.Should().Be("ok=1 failed=0");
        result.StdErr.Should().BeEmpty();
        ComputeSha256(harness.DatabasePath).Should().Be(beforeHash);
        AssertNoJournalFiles(harness.DatabasePath);
    }

    [Fact]
    public async Task Execute_DatabaseWithWalSidecar_FailsClosedInsteadOfIgnoringIt()
    {
        await using var harness = new CliTestHarness("cli-verify-connectors-wal-sidecar");
        await SeedCredentialsAsync(harness.DatabasePath, CorrectKey, "database-secret");
        var keyPath = await WriteKeyFileAsync(harness.DataDirectory, CorrectKey);
        var walPath = $"{harness.DatabasePath}-wal";
        await File.WriteAllTextAsync(walPath, "uncheckpointed-test-state");
        var walHash = ComputeSha256(walPath);

        var result = await harness.RunAsync(
            $"--verify-connectors --database \"{harness.DatabasePath}\" --key-file \"{keyPath}\"");

        result.ExitCode.Should().Be(ExitCodes.Failure);
        result.StdOut.Should().BeEmpty();
        result.StdErr.Should().Contain("Connector database is unavailable");
        ComputeSha256(walPath).Should().Be(walHash);
    }

    [Fact]
    public async Task VerifyDatabaseAsync_DatabaseHashChanges_FailsBeforeReturningCounts()
    {
        await using var harness = new CliTestHarness("cli-verify-connectors-hash-drift");
        await SeedCredentialsAsync(harness.DatabasePath, CorrectKey, "stable-secret");
        var hashCallCount = 0;
        Task<byte[]> HashDatabaseAsync(string _)
        {
            var marker = Interlocked.Increment(ref hashCallCount);
            return Task.FromResult(Enumerable.Repeat((byte)marker, 32).ToArray());
        }

        var action = async () => await ConnectorVerificationCommand.VerifyDatabaseAsync(
            harness.DatabasePath,
            new AesCredentialEncryptionService(CorrectKey),
            HashDatabaseAsync);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*changed during connector verification*");
        hashCallCount.Should().Be(2);
    }

    [Fact]
    public async Task Execute_EmptyDatabase_SucceedsWithExplicitNothingToVerifyMessage()
    {
        await using var harness = new CliTestHarness("cli-verify-connectors-empty");
        var keyPath = await WriteKeyFileAsync(harness.DataDirectory, CorrectKey);

        var result = await harness.RunAsync(
            $"--verify-connectors --database \"{harness.DatabasePath}\" --key-file \"{keyPath}\"");

        result.ExitCode.Should().Be(ExitCodes.Success, result.StdErr);
        result.StdOut.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Should().Be("ok=0 failed=0\nNothing to verify.");
        result.StdErr.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_MissingKey_FailsClosedWithoutBootstrappingOne()
    {
        await using var harness = new CliTestHarness(
            "cli-verify-connectors-missing-key",
            provisionEncryptionKey: false);

        var result = await harness.RunAsync(
            $"--verify-connectors --database \"{harness.DatabasePath}\"");

        result.ExitCode.Should().NotBe(ExitCodes.Success);
        result.StdOut.Should().BeEmpty();
        result.StdErr.Should().Contain("Connector encryption key was not supplied");
        File.Exists(Path.Combine(harness.DataDirectory, "appsettings.local.json"))
            .Should().BeFalse("verification must bypass first-run key generation");
    }

    [Fact]
    public async Task Execute_MissingDatabase_FailsClosedWithoutCreatingIt()
    {
        await using var harness = new CliTestHarness("cli-verify-connectors-missing-db");
        var missingDatabase = Path.Combine(harness.DataDirectory, "missing.db");
        var keyPath = await WriteKeyFileAsync(harness.DataDirectory, CorrectKey);

        var result = await harness.RunAsync(
            $"--verify-connectors --database \"{missingDatabase}\" --key-file \"{keyPath}\"");

        result.ExitCode.Should().Be(ExitCodes.Failure);
        result.StdOut.Should().BeEmpty();
        result.StdErr.Should().Contain("Connector database is unavailable");
        File.Exists(missingDatabase).Should().BeFalse("read-only verification must not create a database");
    }

    [Fact]
    public async Task Execute_DatabaseWithoutConnectorSchema_FailsInsteadOfReportingEmpty()
    {
        await using var harness = new CliTestHarness("cli-verify-connectors-missing-schema");
        var databaseWithoutSchema = Path.Combine(harness.DataDirectory, "unmigrated.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databaseWithoutSchema,
            Pooling = false
        }.ToString();
        await using (var connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE Sentinel (Id INTEGER PRIMARY KEY);";
            await command.ExecuteNonQueryAsync();
        }

        var keyPath = await WriteKeyFileAsync(harness.DataDirectory, CorrectKey);
        var beforeHash = ComputeSha256(databaseWithoutSchema);

        var result = await harness.RunAsync(
            $"--verify-connectors --database \"{databaseWithoutSchema}\" --key-file \"{keyPath}\"");

        result.ExitCode.Should().Be(ExitCodes.Failure);
        result.StdOut.Should().BeEmpty();
        result.StdErr.Should().Contain("Connector database is unavailable");
        ComputeSha256(databaseWithoutSchema).Should().Be(beforeHash);
    }

    private static async Task<IReadOnlyList<string>> SeedCredentialsAsync(
        string databasePath,
        string key,
        params string[] plaintexts)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false
        }.ToString();
        var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
            .UseSqlite(connectionString)
            .Options;
        var encryption = new AesCredentialEncryptionService(key);
        var ciphertexts = plaintexts.Select(encryption.Encrypt).ToArray();

        await using (var context = new TaskdeckDbContext(options))
        {
            var user = new User("connector-verifier", "connector-verifier@example.test", "test-password-hash");
            context.Users.Add(user);

            var connectors = plaintexts
                .Select((_, index) => new IntegrationConnector(
                    $"Verifier {index}",
                    ConnectorType.Custom,
                    ConnectorDirection.Inbound,
                    user.Id))
                .ToArray();
            context.IntegrationConnectors.AddRange(connectors);
            await context.SaveChangesAsync();

            for (var index = 0; index < connectors.Length; index++)
            {
                context.ConnectorCredentials.Add(new ConnectorCredential(
                    connectors[index].Id,
                    user.Id,
                    ConnectorAuthMethod.ApiKey,
                    $"Credential {index}",
                    ciphertexts[index]));
            }

            await context.SaveChangesAsync();
            await context.Database.CloseConnectionAsync();
        }

        await NormalizeJournalModeAsync(databasePath);
        return ciphertexts;
    }

    private static async Task NormalizeJournalModeAsync(string databasePath)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using (var checkpoint = connection.CreateCommand())
        {
            checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            await checkpoint.ExecuteScalarAsync();
        }

        await using (var journalMode = connection.CreateCommand())
        {
            journalMode.CommandText = "PRAGMA journal_mode=DELETE;";
            var mode = (string?)await journalMode.ExecuteScalarAsync();
            mode.Should().Be("delete");
        }
    }

    private static async Task PrepareStandaloneWalHeaderAsync(string databasePath)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false
        }.ToString();

        await using (var connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync();
            await using (var journalMode = connection.CreateCommand())
            {
                journalMode.CommandText = "PRAGMA journal_mode=WAL;";
                var mode = (string?)await journalMode.ExecuteScalarAsync();
                mode.Should().Be("wal");
            }

            await using (var checkpoint = connection.CreateCommand())
            {
                checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                await using var result = await checkpoint.ExecuteReaderAsync();
                (await result.ReadAsync()).Should().BeTrue();
                result.GetInt32(0).Should().Be(0, "the standalone fixture must checkpoint without a busy writer");
            }
        }

        var header = await File.ReadAllBytesAsync(databasePath);
        header.Should().HaveCountGreaterThan(19);
        header[18].Should().Be(2, "the fixture must retain a WAL write-version header");
        header[19].Should().Be(2, "the fixture must retain a WAL read-version header");

        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var sidecar = databasePath + suffix;
            if (File.Exists(sidecar))
            {
                File.Delete(sidecar);
            }
        }
    }

    private static async Task<string> WriteKeyFileAsync(string directory, string key)
    {
        var path = Path.Combine(directory, "connector-encryption.key");
        await File.WriteAllTextAsync(path, key + Environment.NewLine);
        return path;
    }

    private static string ComputeSha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static void AssertNoJournalFiles(string databasePath)
    {
        File.Exists($"{databasePath}-wal").Should().BeFalse();
        File.Exists($"{databasePath}-shm").Should().BeFalse();
        File.Exists($"{databasePath}-journal").Should().BeFalse();
    }

    private sealed class RecordingEncryptionService(Func<string, string> decrypt) : ICredentialEncryptionService
    {
        public List<string> Attempts { get; } = [];

        public string Encrypt(string plaintext) => throw new NotSupportedException();

        public string Decrypt(string ciphertext)
        {
            Attempts.Add(ciphertext);
            return decrypt(ciphertext);
        }
    }
}
