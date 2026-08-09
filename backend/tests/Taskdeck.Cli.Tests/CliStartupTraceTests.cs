using FluentAssertions;
using Xunit;

namespace Taskdeck.Cli.Tests;

public sealed class CliStartupTraceTests
{
    [Fact]
    public void Record_WithoutHarnessTrace_IsNoOp()
    {
        var trace = CliStartupTrace.TryCreate(workingDirectory: null, correlationId: null);

        var action = () => trace.Record(CliStartupTrace.ManagedEntryPhase);

        action.Should().NotThrow();
    }

    [Fact]
    public void Record_WritesOnlyAllowlistedPhasesInOrder()
    {
        using var directory = new TemporaryDirectory();
        const string correlationId = "0123456789abcdef0123456789abcdef";
        var path = CliStartupTrace.TryGetTracePath(directory.Path, correlationId);
        var trace = CliStartupTrace.TryCreate(directory.Path, correlationId);

        trace.Record(CliStartupTrace.ManagedEntryPhase);
        trace.Record("not-an-allowed-phase");
        trace.Record(CliStartupTrace.DispatchBeginPhase);

        var snapshot = CliStartupTrace.ReadSnapshot(path, correlationId);

        snapshot.State.Should().Be("available");
        snapshot.RecordCount.Should().Be(2);
        snapshot.MalformedRecordCount.Should().Be(0);
        snapshot.LastPhase.Should().Be(CliStartupTrace.DispatchBeginPhase);
        snapshot.CorrelationId.Should().Be(correlationId);
    }

    [Fact]
    public void Record_WhenTraceWriteFails_IsFailOpen()
    {
        using var directory = new TemporaryDirectory();
        const string correlationId = "0123456789abcdef0123456789abcdef";
        var trace = CliStartupTrace.TryCreate(directory.Path, correlationId);

        var action = () =>
        {
            trace.Record(CliStartupTrace.ManagedEntryPhase);
            trace.Record(CliStartupTrace.DispatchBeginPhase);
        };

        action.Should().NotThrow();
    }

    [Fact]
    public void ReadSnapshot_WhenTraceExceedsBound_ReturnsFixedOversizedState()
    {
        using var directory = new TemporaryDirectory();
        const string correlationId = "0123456789abcdef0123456789abcdef";
        var path = CliStartupTrace.TryGetTracePath(directory.Path, correlationId);
        File.WriteAllText(path, new string('x', CliStartupTrace.MaximumTraceBytes + 1));

        var snapshot = CliStartupTrace.ReadSnapshot(path, correlationId);

        snapshot.Should().BeSameAs(CliStartupTraceSnapshot.Oversized);
        snapshot.ToDiagnosticString().Should().NotContain("x");
    }

    [Fact]
    public void ReadSnapshot_WhenTraceIsMalformed_ReportsFixedCountersWithoutEchoingContent()
    {
        using var directory = new TemporaryDirectory();
        const string correlationId = "0123456789abcdef0123456789abcdef";
        const string sentinel = "TOP_SECRET_SENTINEL";
        var path = CliStartupTrace.TryGetTracePath(directory.Path, correlationId);
        File.WriteAllText(path, $"v1|{correlationId}|not-an-allowed-phase|0\n{sentinel}\n");

        var snapshot = CliStartupTrace.ReadSnapshot(path, correlationId);

        snapshot.State.Should().Be("malformed");
        snapshot.RecordCount.Should().Be(0);
        snapshot.MalformedRecordCount.Should().Be(2);
        snapshot.ToDiagnosticString().Should().NotContain(sentinel);
    }

    [Fact]
    public void TryGetTracePath_DerivesFixedSafeNameInsideTheWorkingDirectory()
    {
        using var directory = new TemporaryDirectory();
        const string correlationId = "0123456789abcdef0123456789abcdef";

        var path = CliStartupTrace.TryGetTracePath(directory.Path, correlationId);

        path.Should().Be(Path.Combine(directory.Path, $"startup-{correlationId}.trace"));
        Path.GetDirectoryName(path).Should().Be(Path.GetFullPath(directory.Path));
        path.Should().NotContain("TASKDECK_CLI_TEST_TRACE_PATH");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"taskdeck-cli-trace-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
