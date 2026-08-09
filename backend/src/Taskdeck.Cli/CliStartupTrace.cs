using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Taskdeck.Cli;

/// <summary>
/// Test-harness-only startup progress tracing for diagnosing a hung CLI child.
/// The trace is deliberately opt-in, bounded by the reader, and fail-open.
/// </summary>
internal sealed class CliStartupTrace
{
    internal const string CorrelationEnvironmentVariable = "TASKDECK_CLI_TEST_TRACE_CORRELATION";
    internal const int MaximumTraceBytes = 8 * 1024;
    internal const int MaximumTraceRecords = 32;

    internal const string ManagedEntryPhase = "managed-entry";
    internal const string HostBuildBeginPhase = "host-build-begin";
    internal const string HostBuildEndPhase = "host-build-end";
    internal const string MigrationBeginPhase = "migration-begin";
    internal const string MigrationEndPhase = "migration-end";
    internal const string DispatchBeginPhase = "dispatch-begin";
    internal const string DispatchEndPhase = "dispatch-end";
    internal const string DisposalEndPhase = "disposal-end";

    private static readonly HashSet<string> AllowedPhases =
    [
        ManagedEntryPhase,
        HostBuildBeginPhase,
        HostBuildEndPhase,
        MigrationBeginPhase,
        MigrationEndPhase,
        DispatchBeginPhase,
        DispatchEndPhase,
        DisposalEndPhase
    ];

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly string? _path;
    private readonly string? _correlationId;
    private readonly long _startedTimestamp;
    private bool _disabled;

    private CliStartupTrace(string? path, string? correlationId)
    {
        _path = path;
        _correlationId = correlationId;
        _startedTimestamp = Stopwatch.GetTimestamp();
    }

    internal static CliStartupTrace CreateFromTestHarnessEnvironment() =>
        TryCreate(
            Environment.CurrentDirectory,
            Environment.GetEnvironmentVariable(CorrelationEnvironmentVariable));

    internal static CliStartupTrace TryCreate(string? workingDirectory, string? correlationId)
    {
        var path = TryGetTracePath(workingDirectory, correlationId);
        return path is null
            ? new CliStartupTrace(path: null, correlationId: null)
            : new CliStartupTrace(path, correlationId);
    }

    internal static string? TryGetTracePath(string? workingDirectory, string? correlationId)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory) || !IsCorrelationId(correlationId))
        {
            return null;
        }

        try
        {
            var root = Path.GetFullPath(workingDirectory);
            var candidate = Path.GetFullPath(Path.Combine(root, $"startup-{correlationId}.trace"));
            var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;
            return candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
                ? candidate
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    internal void Record(string phase)
    {
        if (_disabled || _path is null || _correlationId is null || !AllowedPhases.Contains(phase))
        {
            return;
        }

        try
        {
            var elapsedTicks = Stopwatch.GetTimestamp() - _startedTimestamp;
            File.AppendAllText(
                _path,
                $"v1|{_correlationId}|{phase}|{elapsedTicks.ToString(CultureInfo.InvariantCulture)}\n",
                StrictUtf8);
        }
        catch (Exception)
        {
            // Diagnostics must never affect the CLI command, output, or exit code.
            _disabled = true;
        }
    }

    internal static CliStartupTraceSnapshot ReadSnapshot(string? path, string expectedCorrelationId)
    {
        if (string.IsNullOrWhiteSpace(path) || !IsCorrelationId(expectedCorrelationId))
        {
            return CliStartupTraceSnapshot.Unavailable;
        }

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);
            var buffer = new byte[MaximumTraceBytes + 1];
            var totalRead = 0;
            while (totalRead < buffer.Length)
            {
                var bytesRead = stream.Read(buffer, totalRead, buffer.Length - totalRead);
                if (bytesRead == 0)
                {
                    break;
                }

                totalRead += bytesRead;
            }

            if (totalRead > MaximumTraceBytes)
            {
                return CliStartupTraceSnapshot.Oversized;
            }

            var content = StrictUtf8.GetString(buffer, 0, totalRead);
            var validRecordCount = 0;
            var malformedRecordCount = 0;
            string? lastPhase = null;

            foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (validRecordCount + malformedRecordCount >= MaximumTraceRecords)
                {
                    return CliStartupTraceSnapshot.TooManyRecords;
                }

                if (!TryParseRecord(line, expectedCorrelationId, out var phase))
                {
                    malformedRecordCount++;
                    continue;
                }

                validRecordCount++;
                lastPhase = phase;
            }

            return new CliStartupTraceSnapshot(
                malformedRecordCount == 0 ? "available" : "malformed",
                expectedCorrelationId,
                validRecordCount,
                malformedRecordCount,
                lastPhase);
        }
        catch (Exception)
        {
            return CliStartupTraceSnapshot.Unavailable;
        }
    }

    private static bool TryParseRecord(string line, string expectedCorrelationId, out string? phase)
    {
        phase = null;
        var parts = line.Split('|');
        if (parts.Length != 4 ||
            !string.Equals(parts[0], "v1", StringComparison.Ordinal) ||
            !string.Equals(parts[1], expectedCorrelationId, StringComparison.Ordinal) ||
            !AllowedPhases.Contains(parts[2]) ||
            !long.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var elapsedTicks) ||
            elapsedTicks < 0)
        {
            return false;
        }

        phase = parts[2];
        return true;
    }

    private static bool IsCorrelationId(string? correlationId) =>
        correlationId is { Length: 32 } && correlationId.All(Uri.IsHexDigit);
}

internal sealed record CliStartupTraceSnapshot(
    string State,
    string? CorrelationId,
    int RecordCount,
    int MalformedRecordCount,
    string? LastPhase)
{
    internal static CliStartupTraceSnapshot Unavailable { get; } = new("unavailable", null, 0, 0, null);
    internal static CliStartupTraceSnapshot Oversized { get; } = new("oversized", null, 0, 0, null);
    internal static CliStartupTraceSnapshot TooManyRecords { get; } = new("too-many-records", null, 0, 0, null);

    internal string ToDiagnosticString() =>
        $"trace={State};records={RecordCount};malformed={MalformedRecordCount};" +
        $"last={LastPhase ?? "none"};correlation={CorrelationId ?? "none"}";
}
