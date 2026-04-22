using System.Diagnostics;
using FluentAssertions;
using Xunit;

namespace Taskdeck.Cli.Tests;

public class CommandDispatcherTests
{
    [Fact]
    public async Task NoArgs_PrintsHelpAndReturnsUsageExitCode()
    {
        await using var harness = new CliHarness();

        var result = await harness.RunAsync("");

        result.ExitCode.Should().Be(2);
        result.StdOut.Should().Contain("Taskdeck CLI");
        result.StdOut.Should().Contain("Usage:");
    }

    [Fact]
    public async Task Help_PrintsHelpAndReturnsSuccess()
    {
        await using var harness = new CliHarness();

        var result = await harness.RunAsync("help");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("Taskdeck CLI");
        result.StdOut.Should().Contain("boards");
        result.StdOut.Should().Contain("columns");
        result.StdOut.Should().Contain("cards");
        result.StdOut.Should().Contain("api-key");
    }

    [Fact]
    public async Task UnknownCommandGroup_ReturnsUsageError()
    {
        await using var harness = new CliHarness();

        var result = await harness.RunAsync("nonexistent");

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("Unknown command group");
    }

    [Fact]
    public async Task ExitCodes_SuccessIsZero()
    {
        await using var harness = new CliHarness();

        // "help" should return exit code 0
        var result = await harness.RunAsync("help");

        result.ExitCode.Should().Be(0);
    }

    [Fact]
    public async Task ExitCodes_UsageIsTwo()
    {
        await using var harness = new CliHarness();

        // Unknown command should return exit code 2
        var result = await harness.RunAsync("nonexistent");

        result.ExitCode.Should().Be(2);
    }

    private sealed class CliHarness : IAsyncDisposable
    {
        private readonly string _repoRoot;
        private readonly string _databasePath;
        private readonly string _connectionString;

        public CliHarness()
        {
            _repoRoot = FindRepoRoot();
            _databasePath = Path.Combine(Path.GetTempPath(), $"taskdeck-cli-dispatch-tests-{Guid.NewGuid():N}.db");
            _connectionString = $"Data Source={_databasePath}";
        }

        public async Task<CliCommandResult> RunAsync(string arguments)
        {
            var cliDllPath = ResolveCliDllPath(_repoRoot);
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = string.IsNullOrWhiteSpace(arguments)
                    ? $"\"{cliDllPath}\""
                    : $"\"{cliDllPath}\" {arguments}",
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
