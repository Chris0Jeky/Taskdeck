using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Taskdeck.Cli.Tests;

public class ApiKeyCommandTests
{
    [Fact]
    public async Task ApiKeyCreate_ReturnsJsonWithTdskPrefix()
    {
        await using var harness = new CliHarness();

        var result = await harness.RunAsync("api-key create --name \"Test Key\"");

        result.ExitCode.Should().Be(0, result.StdErr);
        using var doc = JsonDocument.Parse(result.StdOut);
        doc.RootElement.GetProperty("key").GetString().Should().StartWith("tdsk_");
        doc.RootElement.GetProperty("name").GetString().Should().Be("Test Key");
        doc.RootElement.GetProperty("message").GetString().Should().Contain("cannot be retrieved");
    }

    [Fact]
    public async Task ApiKeyCreate_WithExpires_SetsExpiresAt()
    {
        await using var harness = new CliHarness();

        var result = await harness.RunAsync("api-key create --name \"Expiring\" --expires 90d");

        result.ExitCode.Should().Be(0, result.StdErr);
        using var doc = JsonDocument.Parse(result.StdOut);
        doc.RootElement.GetProperty("expiresAt").GetDateTimeOffset()
            .Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(90), TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task ApiKeyCreate_WithNumericExpires_AcceptsDaysWithoutSuffix()
    {
        await using var harness = new CliHarness();

        var result = await harness.RunAsync("api-key create --name \"NumExpiry\" --expires 30");

        result.ExitCode.Should().Be(0, result.StdErr);
        using var doc = JsonDocument.Parse(result.StdOut);
        doc.RootElement.GetProperty("expiresAt").GetDateTimeOffset()
            .Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(30), TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task ApiKeyCreate_MissingName_ReturnsUsageError()
    {
        await using var harness = new CliHarness();

        var result = await harness.RunAsync("api-key create");

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("--name");
    }

    [Fact]
    public async Task ApiKeyCreate_InvalidExpires_ReturnsUsageError()
    {
        await using var harness = new CliHarness();

        var result = await harness.RunAsync("api-key create --name \"Bad\" --expires abc");

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("Invalid --expires");
    }

    [Fact]
    public async Task ApiKeyList_ReturnsCreatedKeys()
    {
        await using var harness = new CliHarness();

        await harness.RunAsync("api-key create --name \"List Key A\"");
        await harness.RunAsync("api-key create --name \"List Key B\"");

        var result = await harness.RunAsync("api-key list");

        result.ExitCode.Should().Be(0, result.StdErr);
        using var doc = JsonDocument.Parse(result.StdOut);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);

        var names = doc.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("name").GetString())
            .ToList();
        names.Should().Contain("List Key A");
        names.Should().Contain("List Key B");
    }

    [Fact]
    public async Task ApiKeyList_ShowsKeyPrefixNotFullKey()
    {
        await using var harness = new CliHarness();

        await harness.RunAsync("api-key create --name \"Prefix Key\"");

        var result = await harness.RunAsync("api-key list");

        result.ExitCode.Should().Be(0, result.StdErr);
        using var doc = JsonDocument.Parse(result.StdOut);
        var firstKey = doc.RootElement.EnumerateArray().First();
        firstKey.GetProperty("keyPrefix").GetString()!.Length.Should().Be(8);
        firstKey.GetProperty("keyPrefix").GetString().Should().StartWith("tdsk_");
    }

    [Fact]
    public async Task ApiKeyRevoke_ByName_SetsRevokedStatus()
    {
        await using var harness = new CliHarness();

        await harness.RunAsync("api-key create --name \"Revoke Me\"");

        var revokeResult = await harness.RunAsync("api-key revoke --name \"Revoke Me\"");
        revokeResult.ExitCode.Should().Be(0, revokeResult.StdErr);
        using var revokeDoc = JsonDocument.Parse(revokeResult.StdOut);
        revokeDoc.RootElement.GetProperty("status").GetString().Should().Be("ok");

        // Verify in list
        var listResult = await harness.RunAsync("api-key list");
        using var listDoc = JsonDocument.Parse(listResult.StdOut);
        var revokedKey = listDoc.RootElement.EnumerateArray()
            .First(e => e.GetProperty("name").GetString() == "Revoke Me");
        revokedKey.GetProperty("isActive").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ApiKeyRevoke_ById_SetsRevokedStatus()
    {
        await using var harness = new CliHarness();

        var createResult = await harness.RunAsync("api-key create --name \"Revoke By Id\"");
        using var createDoc = JsonDocument.Parse(createResult.StdOut);
        var keyId = createDoc.RootElement.GetProperty("id").GetGuid();

        var revokeResult = await harness.RunAsync($"api-key revoke --id {keyId}");
        revokeResult.ExitCode.Should().Be(0, revokeResult.StdErr);
    }

    [Fact]
    public async Task ApiKeyRevoke_NonexistentName_ReturnsFailure()
    {
        await using var harness = new CliHarness();

        var result = await harness.RunAsync("api-key revoke --name \"Does Not Exist\"");

        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Contain("No active API key found");
    }

    [Fact]
    public async Task ApiKeyRevoke_MissingIdentifier_ReturnsUsageError()
    {
        await using var harness = new CliHarness();

        var result = await harness.RunAsync("api-key revoke");

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("--name");
    }

    [Fact]
    public async Task ApiKey_UnknownSubcommand_ReturnsUsageError()
    {
        await using var harness = new CliHarness();

        var result = await harness.RunAsync("api-key unknown");

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("Unknown api-key command");
    }

    /// <summary>
    /// Shared harness that runs the CLI as a subprocess against an ephemeral database.
    /// </summary>
    private sealed class CliHarness : IAsyncDisposable
    {
        private readonly string _repoRoot;
        private readonly string _databasePath;
        private readonly string _connectionString;

        public CliHarness()
        {
            _repoRoot = FindRepoRoot();
            _databasePath = Path.Combine(Path.GetTempPath(), $"taskdeck-cli-apikey-tests-{Guid.NewGuid():N}.db");
            _connectionString = $"Data Source={_databasePath}";
        }

        public async Task<CliCommandResult> RunAsync(string arguments)
        {
            var cliDllPath = ResolveCliDllPath(_repoRoot);
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{cliDllPath}\" {arguments}",
                WorkingDirectory = _repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            startInfo.Environment["TASKDECK_CONNECTION_STRING"] = _connectionString;
            startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var stdOut = await process.StandardOutput.ReadToEndAsync();
            var stdErr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            return new CliCommandResult(process.ExitCode, stdOut.Trim(), stdErr.Trim());
        }

        public ValueTask DisposeAsync()
        {
            foreach (var path in new[] { _databasePath, $"{_databasePath}-wal", $"{_databasePath}-shm", $"{_databasePath}-journal" })
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                catch (IOException) { }
            }

            return ValueTask.CompletedTask;
        }

        private static string FindRepoRoot()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);

            while (current != null)
            {
                var solutionPath = Path.Combine(current.FullName, "backend", "Taskdeck.sln");
                if (File.Exists(solutionPath))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new InvalidOperationException("Could not locate repository root from test execution directory.");
        }

        private static string ResolveCliDllPath(string repoRoot)
        {
            var cliProjectBin = Path.Combine(repoRoot, "backend", "src", "Taskdeck.Cli", "bin");
            var debugPath = Path.Combine(cliProjectBin, "Debug", "net8.0", "Taskdeck.Cli.dll");
            if (File.Exists(debugPath))
            {
                return debugPath;
            }

            var releasePath = Path.Combine(cliProjectBin, "Release", "net8.0", "Taskdeck.Cli.dll");
            if (File.Exists(releasePath))
            {
                return releasePath;
            }

            throw new FileNotFoundException("Taskdeck.Cli.dll was not found in Debug or Release output directories.");
        }
    }

    private sealed record CliCommandResult(int ExitCode, string StdOut, string StdErr);
}
