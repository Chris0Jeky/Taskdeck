using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Taskdeck.Api.Tests;

public class TestAssemblyDiagnosticsTests
{
    [Fact]
    public void CreateIfEnabled_IsDisabledByDefault()
    {
        TestAssemblyDiagnostics.CreateIfEnabled(
                outputPath: null,
                captureSnapshot: () => Snapshot(1),
                getTimestamp: () => 0,
                timestampFrequency: 1)
            .Should().BeNull();
        TestAssemblyDiagnostics.CreateIfEnabled(
                outputPath: " ",
                captureSnapshot: () => Snapshot(1),
                getTimestamp: () => 0,
                timestampFrequency: 1)
            .Should().BeNull();
    }

    [Fact]
    public void EmitNow_WritesExactAllowlistedSchemaAndAggregates()
    {
        using var output = TemporaryOutput.Create();
        var snapshots = new Queue<TestAssemblyDiagnostics.ProcessSnapshot>([Snapshot(10), Snapshot(20)]);
        var timestamps = new Queue<long>([100, 110, 200, 250]);
        var diagnostics = new TestAssemblyDiagnostics(
            output.Path,
            () => snapshots.Dequeue(),
            () => timestamps.Dequeue(),
            timestampFrequency: 1_000);

        diagnostics.CompleteConfigureServices(diagnostics.BeginConfigureServices());
        diagnostics.CompleteDatabaseMigrate(diagnostics.BeginDatabaseMigrate());
        diagnostics.EmitNow();

        var bytes = File.ReadAllBytes(output.Path);
        bytes.Length.Should().BeLessThanOrEqualTo(TestAssemblyDiagnostics.MaxJsonBytes);

        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        PropertyNames(root).Should().BeEquivalentTo(
            ["schemaVersion", "beforeFirstConfigureServices", "atProcessExit", "configureServices", "databaseMigrate"]);
        root.GetProperty("schemaVersion").GetInt32().Should().Be(1);
        PropertyNames(root.GetProperty("beforeFirstConfigureServices")).Should().BeEquivalentTo(ProcessSnapshotProperties);
        PropertyNames(root.GetProperty("atProcessExit")).Should().BeEquivalentTo(ProcessSnapshotProperties);
        PropertyNames(root.GetProperty("configureServices")).Should().BeEquivalentTo(AggregateProperties);
        PropertyNames(root.GetProperty("databaseMigrate")).Should().BeEquivalentTo(AggregateProperties);
        root.GetProperty("configureServices").GetProperty("attemptCount").GetInt64().Should().Be(1);
        root.GetProperty("configureServices").GetProperty("completedCount").GetInt64().Should().Be(1);
        root.GetProperty("configureServices").GetProperty("durationMillisecondsSum").GetInt64().Should().Be(10);
        root.GetProperty("configureServices").GetProperty("durationMillisecondsMax").GetInt64().Should().Be(10);
        root.GetProperty("databaseMigrate").GetProperty("attemptCount").GetInt64().Should().Be(1);
        root.GetProperty("databaseMigrate").GetProperty("completedCount").GetInt64().Should().Be(1);
        root.GetProperty("databaseMigrate").GetProperty("durationMillisecondsSum").GetInt64().Should().Be(50);
        root.GetProperty("databaseMigrate").GetProperty("durationMillisecondsMax").GetInt64().Should().Be(50);
    }

    [Fact]
    public async Task ConcurrentCompletions_KeepAggregateNumbersNonnegative()
    {
        using var output = TemporaryOutput.Create();
        var clock = 0L;
        var diagnostics = new TestAssemblyDiagnostics(
            output.Path,
            () => Snapshot(1),
            () => Interlocked.Increment(ref clock),
            timestampFrequency: 1_000);

        const int operations = 64;
        await Task.WhenAll(Enumerable.Range(0, operations).Select(_ => Task.Run(() =>
        {
            diagnostics.CompleteConfigureServices(diagnostics.BeginConfigureServices());
            diagnostics.CompleteDatabaseMigrate(diagnostics.BeginDatabaseMigrate());
        })));
        diagnostics.EmitNow();

        using var document = JsonDocument.Parse(File.ReadAllBytes(output.Path));
        foreach (var aggregateName in new[] { "configureServices", "databaseMigrate" })
        {
            var aggregate = document.RootElement.GetProperty(aggregateName);
            aggregate.GetProperty("attemptCount").GetInt64().Should().Be(operations);
            aggregate.GetProperty("completedCount").GetInt64().Should().Be(operations);
            aggregate.GetProperty("durationMillisecondsSum").GetInt64().Should().BeGreaterThanOrEqualTo(0);
            aggregate.GetProperty("durationMillisecondsMax").GetInt64().Should().BeGreaterThanOrEqualTo(0);
            aggregate.GetProperty("durationMillisecondsMax").GetInt64()
                .Should().BeLessThanOrEqualTo(aggregate.GetProperty("durationMillisecondsSum").GetInt64());
        }
    }

    [Fact]
    public void EmitNow_ReplacesOutputAtomically()
    {
        using var output = TemporaryOutput.Create();
        File.WriteAllText(output.Path, "partial");
        var diagnostics = new TestAssemblyDiagnostics(output.Path, () => Snapshot(1), () => 0, 1);

        diagnostics.EmitNow();

        File.Exists(output.Path + ".tmp").Should().BeFalse();
        using var document = JsonDocument.Parse(File.ReadAllBytes(output.Path));
        document.RootElement.GetProperty("schemaVersion").GetInt32().Should().Be(1);
    }

    [Fact]
    public void EmitNow_ExcludesSensitiveFixtureContent()
    {
        var sensitiveFixtures = new[]
        {
            "private board title",
            "bearer token",
            "test@example.invalid",
            @"C:\sensitive\test-output.json",
            "Data Source=private-test.db"
        };
        using var output = TemporaryOutput.Create();
        var diagnostics = new TestAssemblyDiagnostics(output.Path, () => Snapshot(1), () => 0, 1);

        diagnostics.EmitNow();

        var json = File.ReadAllText(output.Path);
        foreach (var sensitiveFixture in sensitiveFixtures)
        {
            json.Should().NotContain(sensitiveFixture);
        }
    }

    private static readonly string[] ProcessSnapshotProperties =
    [
        "cpuMilliseconds",
        "workingSetBytes",
        "managedHeapBytes",
        "allocatedBytes",
        "gen0Collections",
        "gen1Collections",
        "gen2Collections"
    ];

    private static readonly string[] AggregateProperties =
    [
        "attemptCount",
        "completedCount",
        "durationMillisecondsSum",
        "durationMillisecondsMax"
    ];

    private static IEnumerable<string> PropertyNames(JsonElement element) =>
        element.EnumerateObject().Select(property => property.Name);

    private static TestAssemblyDiagnostics.ProcessSnapshot Snapshot(int value) =>
        new(value, value, value, value, value, value, value);

    private sealed class TemporaryOutput : IDisposable
    {
        private readonly string _directory;

        private TemporaryOutput(string directory)
        {
            _directory = directory;
            Path = System.IO.Path.Combine(directory, "diagnostics.json");
        }

        public string Path { get; }

        public static TemporaryOutput Create()
        {
            var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"taskdeck-diagnostics-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            return new TemporaryOutput(directory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
