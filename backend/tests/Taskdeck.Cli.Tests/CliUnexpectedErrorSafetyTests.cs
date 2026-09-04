using FluentAssertions;
using Taskdeck.Application.Services;
using Taskdeck.Cli.Commands;
using Taskdeck.Domain.Exceptions;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Cli.Tests;

/// <summary>
/// #2351: an unknown exception must never place its raw text, a stack trace, a file path,
/// SQL/constraint detail, a provider URL or a token on the standalone CLI's stdout/stderr.
/// The full exception stays exactly once in the protected startup-trace sink, and deliberate
/// domain/validation messages keep printing unchanged.
/// </summary>
[Collection("Console Tests")]
public sealed class CliUnexpectedErrorSafetyTests
{
    private const string SecretToken = "sk-live-ABC123";
    private const string WindowsPath = @"C:\Users\operator\AppData\Local\Taskdeck\taskdeck.db";
    private const string SqliteConstraint =
        "SQLite Error 19: 'UNIQUE constraint failed: Cards.BoardId, Cards.Title'";
    private const string ProviderUrl = "https://api.openai.example/v1/chat/completions";
    private const string CorrelationId = "0123456789abcdef0123456789abcdef";

    private static Exception CreateLeakyException() =>
        new InvalidOperationException(
            $"Persisting card failed against {WindowsPath}: {SqliteConstraint}",
            new HttpRequestException(
                $"POST {ProviderUrl} rejected api_key={SecretToken}"));

    private static IEnumerable<string> SensitiveFragments =>
        [SecretToken, WindowsPath, SqliteConstraint, ProviderUrl, "UNIQUE constraint", "api_key="];

    [Fact]
    public void Handle_WithoutTrace_PrintsOnlyTheStableGenericLine()
    {
        using var stderr = new StringWriter();

        var exitCode = CliUnexpectedFailure.Handle(CreateLeakyException(), trace: null, stderr);

        exitCode.Should().Be(ExitCodes.Failure);
        var output = stderr.ToString();
        output.Should().Contain($"Error [{CliUnexpectedFailure.ErrorCode}]");
        output.Should().Contain(SensitiveDataRedactor.GenericUnexpectedFailureMessage);
        output.Should().NotContain("InvalidOperationException");
        output.Should().NotContain("   at ");
        foreach (var fragment in SensitiveFragments)
        {
            output.Should().NotContain(fragment);
        }
    }

    [Fact]
    public void Handle_WithoutTrace_SaysDiagnosticsWereNotCaptured()
    {
        using var stderr = new StringWriter();

        CliUnexpectedFailure.Handle(CreateLeakyException(), trace: null, stderr);

        stderr.ToString().Should().Contain(CliUnexpectedFailure.DiagnosticsUnavailableNotice);
    }

    [Fact]
    public void Handle_WithTrace_ShowsCorrelationReferenceAndKeepsOutputSafe()
    {
        using var directory = new TemporaryDirectory();
        var trace = CliStartupTrace.TryCreate(directory.Path, CorrelationId);
        using var stderr = new StringWriter();

        var exitCode = CliUnexpectedFailure.Handle(CreateLeakyException(), trace, stderr);

        exitCode.Should().Be(ExitCodes.Failure);
        var output = stderr.ToString();
        output.Should().Contain(SensitiveDataRedactor.GenericUnexpectedFailureMessage);
        output.Should().Contain(CorrelationId);
        output.Should().NotContain(CliUnexpectedFailure.DiagnosticsUnavailableNotice);
        foreach (var fragment in SensitiveFragments)
        {
            output.Should().NotContain(fragment);
        }
    }

    [Fact]
    public void Handle_WithTrace_WritesTheFullExceptionExactlyOnceToTheProtectedSink()
    {
        using var directory = new TemporaryDirectory();
        var trace = CliStartupTrace.TryCreate(directory.Path, CorrelationId);
        using var stderr = new StringWriter();

        CliUnexpectedFailure.Handle(CreateLeakyException(), trace, stderr);

        var failurePath = Path.Combine(directory.Path, $"startup-{CorrelationId}.failure");
        File.Exists(failurePath).Should().BeTrue();
        var captured = File.ReadAllText(failurePath);
        foreach (var fragment in SensitiveFragments)
        {
            captured.Should().Contain(fragment);
        }

        CountOccurrences(captured, SecretToken).Should().Be(1);
        CountOccurrences(captured, SqliteConstraint).Should().Be(1);

        // The trace stream marks the failure so a reader can correlate the two files.
        var tracePath = CliStartupTrace.TryGetTracePath(directory.Path, CorrelationId);
        var snapshot = CliStartupTrace.ReadSnapshot(tracePath, CorrelationId);
        snapshot.State.Should().Be("available");
        snapshot.LastPhase.Should().Be(CliStartupTrace.UnexpectedFailurePhase);
        snapshot.MalformedRecordCount.Should().Be(0);
    }

    [Fact]
    public void UnexpectedFailureMessage_IsTheCanonicalRedactorConstant()
    {
        CliUnexpectedFailure.Message.Should().Be(SensitiveDataRedactor.GenericUnexpectedFailureMessage);
        CliUnexpectedFailure.ErrorCode.Should().Be(ErrorCodes.UnexpectedError);
    }

    [Fact]
    public void DeliberateDomainFailure_StillPrintsItsOwnMessage()
    {
        var domainException = new DomainException(ErrorCodes.NotFound, "Board 'alpha' was not found.");
        using var stderr = new StringWriter();
        var originalErr = Console.Error;
        Console.SetError(stderr);
        try
        {
            var exitCode = ConsoleOutput.PrintFailure(domainException.ErrorCode, domainException.Message);

            exitCode.Should().Be(ExitCodes.Failure);
        }
        finally
        {
            Console.SetError(originalErr);
        }

        var output = stderr.ToString();
        output.Should().Contain("Board 'alpha' was not found.");
        output.Should().Contain(ErrorCodes.NotFound);
        output.Should().NotContain(SensitiveDataRedactor.GenericUnexpectedFailureMessage);
    }

    [Fact]
    public void DeliberateUsageFailure_StillPrintsItsOwnMessage()
    {
        using var stderr = new StringWriter();
        var originalErr = Console.Error;
        Console.SetError(stderr);
        try
        {
            var exitCode = ConsoleOutput.PrintUsageError(
                "Invalid --expires value.",
                "taskdeck api-key create --name <name>");

            exitCode.Should().Be(ExitCodes.Usage);
        }
        finally
        {
            Console.SetError(originalErr);
        }

        var output = stderr.ToString();
        output.Should().Contain("Invalid --expires value.");
        output.Should().Contain("taskdeck api-key create --name <name>");
        output.Should().NotContain(SensitiveDataRedactor.GenericUnexpectedFailureMessage);
    }

    [Fact]
    public void FailureSink_IsOwnerReadWriteOnly_OnPosix()
    {
        if (OperatingSystem.IsWindows())
        {
            // NTFS ACL inheritance governs access on Windows; there is no Unix mode to assert.
            return;
        }

        using var directory = new TemporaryDirectory();
        var trace = CliStartupTrace.TryCreate(directory.Path, CorrelationId);
        using var stderr = new StringWriter();

        CliUnexpectedFailure.Handle(CreateLeakyException(), trace, stderr);

        var failurePath = Path.Combine(directory.Path, $"startup-{CorrelationId}.failure");
        File.GetUnixFileMode(failurePath)
            .Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [Fact]
    public void Handle_WhenTraceDisabledItself_DoesNotShowACorrelationReference()
    {
        // Occupy the trace path with a directory so the first write fails and the trace disables
        // itself; the CLI must not then advertise a reference to a record it did not keep.
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(directory.Path, $"startup-{CorrelationId}.trace"));
        var trace = CliStartupTrace.TryCreate(directory.Path, CorrelationId);
        trace.Record(CliStartupTrace.ManagedEntryPhase);
        using var stderr = new StringWriter();

        var exitCode = CliUnexpectedFailure.Handle(CreateLeakyException(), trace, stderr);

        exitCode.Should().Be(ExitCodes.Failure);
        trace.CorrelationId.Should().BeNull();
        var output = stderr.ToString();
        output.Should().NotContain(CorrelationId);
        output.Should().Contain(CliUnexpectedFailure.DiagnosticsUnavailableNotice);
    }

    [Fact]
    public void Handle_WithPreMigrationBackupFailure_KeepsItsDeliberateOperatorMessage()
    {
        var exception = new PreMigrationBackupException(
            "Pre-migration snapshot could not be written; migration was blocked to protect your data.",
            new IOException("disk full"));
        using var stderr = new StringWriter();

        var exitCode = CliUnexpectedFailure.Handle(exception, trace: null, stderr);

        exitCode.Should().Be(ExitCodes.Failure);
        var output = stderr.ToString();
        output.Should().Contain(CliUnexpectedFailure.PreMigrationBackupErrorCode);
        output.Should().Contain("migration was blocked to protect your data.");
        output.Should().NotContain(SensitiveDataRedactor.GenericUnexpectedFailureMessage);
        output.Should().NotContain("   at ");
        output.Should().NotContain("IOException");
    }

    /// <summary>
    /// End-to-end proof that the boundary is actually wired into the entry point: an
    /// unopenable database makes startup throw from inside the host, and the real process
    /// must still exit with the failure code and a safe stderr.
    /// </summary>
    [Fact]
    public async Task RealCli_WhenStartupThrows_ExitsWithFailureAndSafeStdErr()
    {
        await using var harness = new CliTestHarness("cli-unexpected-failure");
        var unopenableDirectory = Path.Combine(
            Path.GetTempPath(),
            "taskdeck-missing-" + Guid.NewGuid().ToString("N"));
        var unopenableDatabase = Path.Combine(unopenableDirectory, "sk-live-ABC123.db");

        var result = await harness.RunAsync(
            "boards list",
            new Dictionary<string, string?>
            {
                ["TASKDECK_CONNECTION_STRING"] = $"Data Source={unopenableDatabase}"
            });

        result.ExitCode.Should().Be(ExitCodes.Failure, result.StdErr);
        result.StdErr.Should().Contain(SensitiveDataRedactor.GenericUnexpectedFailureMessage);
        result.StdErr.Should().NotContain(SecretToken);
        result.StdErr.Should().NotContain("SQLite Error");
        result.StdErr.Should().NotContain("   at ");
        result.StdOut.Should().NotContain(SecretToken);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = haystack.IndexOf(needle, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "taskdeck-cli-failure-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (Exception)
            {
                // Best-effort cleanup.
            }
        }
    }
}
