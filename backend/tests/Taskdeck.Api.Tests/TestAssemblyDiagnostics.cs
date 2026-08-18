using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Emits a bounded, content-free process summary for the API integration test assembly when explicitly enabled.
/// </summary>
internal sealed class TestAssemblyDiagnostics
{
    internal const string OutputPathEnvironmentVariable = "TASKDECK_API_TEST_ASSEMBLY_DIAGNOSTICS_PATH";
    internal const int MaxJsonBytes = 16 * 1024;

    private static readonly object ActivationLock = new();
    private static TestAssemblyDiagnostics? s_active;

    private readonly string _outputPath;
    private readonly Func<ProcessSnapshot> _captureSnapshot;
    private readonly Func<long> _getTimestamp;
    private readonly long _timestampFrequency;
    private readonly ProcessSnapshot _beforeFirstConfigureServices;

    private long _configureServicesAttempts;
    private long _configureServicesCompleted;
    private long _configureServicesDurationMillisecondsSum;
    private long _configureServicesDurationMillisecondsMax;
    private long _databaseMigrateAttempts;
    private long _databaseMigrateCompleted;
    private long _databaseMigrateDurationMillisecondsSum;
    private long _databaseMigrateDurationMillisecondsMax;

    internal TestAssemblyDiagnostics(
        string outputPath,
        Func<ProcessSnapshot> captureSnapshot,
        Func<long> getTimestamp,
        long timestampFrequency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(captureSnapshot);
        ArgumentNullException.ThrowIfNull(getTimestamp);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timestampFrequency);

        _outputPath = outputPath;
        _captureSnapshot = captureSnapshot;
        _getTimestamp = getTimestamp;
        _timestampFrequency = timestampFrequency;
        _beforeFirstConfigureServices = captureSnapshot();
    }

    internal static TestAssemblyDiagnostics? ActivateIfConfigured()
    {
        var active = Volatile.Read(ref s_active);
        if (active is not null)
        {
            return active;
        }

        var outputPath = Environment.GetEnvironmentVariable(OutputPathEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return null;
        }

        lock (ActivationLock)
        {
            active = s_active;
            if (active is not null)
            {
                return active;
            }

            active = CreateIfEnabled(
                outputPath,
                CaptureProcessSnapshot,
                Stopwatch.GetTimestamp,
                Stopwatch.Frequency);
            if (active is null)
            {
                return null;
            }

            Volatile.Write(ref s_active, active);
            AppDomain.CurrentDomain.ProcessExit += static (_, _) => Volatile.Read(ref s_active)?.EmitNow();
            return active;
        }
    }

    internal static TestAssemblyDiagnostics? CreateIfEnabled(
        string? outputPath,
        Func<ProcessSnapshot> captureSnapshot,
        Func<long> getTimestamp,
        long timestampFrequency)
    {
        return string.IsNullOrWhiteSpace(outputPath)
            ? null
            : new TestAssemblyDiagnostics(outputPath, captureSnapshot, getTimestamp, timestampFrequency);
    }

    internal long BeginConfigureServices()
    {
        Interlocked.Increment(ref _configureServicesAttempts);
        return _getTimestamp();
    }

    internal void CompleteConfigureServices(long startedTimestamp)
    {
        RecordCompletion(
            startedTimestamp,
            ref _configureServicesCompleted,
            ref _configureServicesDurationMillisecondsSum,
            ref _configureServicesDurationMillisecondsMax);
    }

    internal long BeginDatabaseMigrate()
    {
        Interlocked.Increment(ref _databaseMigrateAttempts);
        return _getTimestamp();
    }

    internal void CompleteDatabaseMigrate(long startedTimestamp)
    {
        RecordCompletion(
            startedTimestamp,
            ref _databaseMigrateCompleted,
            ref _databaseMigrateDurationMillisecondsSum,
            ref _databaseMigrateDurationMillisecondsMax);
    }

    // The process-exit callback deliberately only calls this deterministic emission seam.
    internal void EmitNow()
    {
        var payload = new DiagnosticsPayload(
            SchemaVersion: 1,
            BeforeFirstConfigureServices: _beforeFirstConfigureServices,
            AtProcessExit: _captureSnapshot(),
            ConfigureServices: SnapshotAggregate(
                _configureServicesAttempts,
                _configureServicesCompleted,
                _configureServicesDurationMillisecondsSum,
                _configureServicesDurationMillisecondsMax),
            DatabaseMigrate: SnapshotAggregate(
                _databaseMigrateAttempts,
                _databaseMigrateCompleted,
                _databaseMigrateDurationMillisecondsSum,
                _databaseMigrateDurationMillisecondsMax));

        var json = JsonSerializer.SerializeToUtf8Bytes(payload, SerializerOptions);
        if (json.Length > MaxJsonBytes)
        {
            return;
        }

        try
        {
            WriteAtomically(json);
        }
        catch (IOException)
        {
            // Diagnostics must not affect test execution or its result.
        }
        catch (UnauthorizedAccessException)
        {
            // Diagnostics must not affect test execution or its result.
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false
    };

    private void RecordCompletion(
        long startedTimestamp,
        ref long completedCount,
        ref long durationMillisecondsSum,
        ref long durationMillisecondsMax)
    {
        var elapsedTicks = _getTimestamp() - startedTimestamp;
        var durationMilliseconds = elapsedTicks <= 0
            ? 0L
            : Math.Max(0L, elapsedTicks * 1000L / _timestampFrequency);

        Interlocked.Increment(ref completedCount);
        Interlocked.Add(ref durationMillisecondsSum, durationMilliseconds);
        UpdateMaximum(ref durationMillisecondsMax, durationMilliseconds);
    }

    private static void UpdateMaximum(ref long maximum, long candidate)
    {
        var observed = Volatile.Read(ref maximum);
        while (candidate > observed)
        {
            var original = Interlocked.CompareExchange(ref maximum, candidate, observed);
            if (original == observed)
            {
                return;
            }

            observed = original;
        }
    }

    private static AggregateSnapshot SnapshotAggregate(
        long attempts,
        long completed,
        long durationMillisecondsSum,
        long durationMillisecondsMax)
    {
        return new AggregateSnapshot(
            Math.Max(0L, Volatile.Read(ref attempts)),
            Math.Max(0L, Volatile.Read(ref completed)),
            Math.Max(0L, Volatile.Read(ref durationMillisecondsSum)),
            Math.Max(0L, Volatile.Read(ref durationMillisecondsMax)));
    }

    private void WriteAtomically(byte[] json)
    {
        var temporaryPath = _outputPath + ".tmp";
        File.WriteAllBytes(temporaryPath, json);
        File.Move(temporaryPath, _outputPath, overwrite: true);
    }

    private static ProcessSnapshot CaptureProcessSnapshot()
    {
        using var process = Process.GetCurrentProcess();
        return new ProcessSnapshot(
            CpuMilliseconds: Math.Max(0L, (long)process.TotalProcessorTime.TotalMilliseconds),
            WorkingSetBytes: Math.Max(0L, process.WorkingSet64),
            ManagedHeapBytes: Math.Max(0L, GC.GetTotalMemory(forceFullCollection: false)),
            AllocatedBytes: Math.Max(0L, GC.GetTotalAllocatedBytes(precise: false)),
            Gen0Collections: Math.Max(0, GC.CollectionCount(0)),
            Gen1Collections: Math.Max(0, GC.CollectionCount(1)),
            Gen2Collections: Math.Max(0, GC.CollectionCount(2)));
    }

    internal sealed record ProcessSnapshot(
        [property: JsonPropertyName("cpuMilliseconds")] long CpuMilliseconds,
        [property: JsonPropertyName("workingSetBytes")] long WorkingSetBytes,
        [property: JsonPropertyName("managedHeapBytes")] long ManagedHeapBytes,
        [property: JsonPropertyName("allocatedBytes")] long AllocatedBytes,
        [property: JsonPropertyName("gen0Collections")] int Gen0Collections,
        [property: JsonPropertyName("gen1Collections")] int Gen1Collections,
        [property: JsonPropertyName("gen2Collections")] int Gen2Collections);

    private sealed record AggregateSnapshot(
        [property: JsonPropertyName("attemptCount")] long AttemptCount,
        [property: JsonPropertyName("completedCount")] long CompletedCount,
        [property: JsonPropertyName("durationMillisecondsSum")] long DurationMillisecondsSum,
        [property: JsonPropertyName("durationMillisecondsMax")] long DurationMillisecondsMax);

    private sealed record DiagnosticsPayload(
        [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
        [property: JsonPropertyName("beforeFirstConfigureServices")] ProcessSnapshot BeforeFirstConfigureServices,
        [property: JsonPropertyName("atProcessExit")] ProcessSnapshot AtProcessExit,
        [property: JsonPropertyName("configureServices")] AggregateSnapshot ConfigureServices,
        [property: JsonPropertyName("databaseMigrate")] AggregateSnapshot DatabaseMigrate);
}
