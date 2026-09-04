using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Taskdeck.Application.Common;
using Taskdeck.Application.Services;

namespace Taskdeck.Cli;

/// <summary>
/// The standalone CLI's always-on diagnostic sink for unexpected failures (#2468).
///
/// The CLI clears every logging provider so stdout stays clean JSON, and the startup trace
/// (<see cref="CliStartupTrace"/>) is harness-only, so before this sink an ordinary operator run
/// retained an unexpected exception nowhere at all. This sink keeps one bounded, redacted record
/// per failure under the CLI's own data directory so a self-hoster can report what happened
/// without the CLI ever printing exception text.
///
/// The record deliberately never holds a raw stack trace or a raw <c>Exception.Message</c>: it
/// carries <see cref="SensitiveDataRedactor.SummarizeException"/> output (redacted, bounded depth
/// and length) and the process arguments passed through <see cref="SensitiveDataRedactor.Redact"/>.
///
/// Every failure mode is fail-open: an unwritable directory, a full disk, a pre-existing file at
/// the target path or a permission error returns false so the caller prints the existing
/// "diagnostics were not captured" notice. Diagnostics never change the command result or the exit
/// code, and no exception text ever reaches stdout or stderr from here.
/// </summary>
internal sealed class CliFailureSink
{
    /// <summary>Directory, relative to the CLI data directory, that holds the records.</summary>
    internal const string DirectoryName = "diagnostics";

    internal const string FileNamePrefix = "cli-failure-";
    internal const string FileNameExtension = ".txt";
    internal const string FileNameSearchPattern = $"{FileNamePrefix}*{FileNameExtension}";

    /// <summary>Hard cap on the bytes a single record may occupy.</summary>
    internal const int MaximumRecordBytes = 8 * 1024;

    /// <summary>Hard cap on how many records the directory retains.</summary>
    internal const int MaximumRecordCount = 20;

    /// <summary>Appended when a record hits <see cref="MaximumRecordBytes"/>.</summary>
    internal const string TruncationMarker = "\n[truncated: record exceeded the 8192-byte bound]\n";

    /// <summary>Length, in lowercase hex characters, of a generated correlation reference.</summary>
    internal const int ReferenceLength = 12;

    private static readonly UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly string? _diagnosticsDirectory;

    private CliFailureSink(string? diagnosticsDirectory) => _diagnosticsDirectory = diagnosticsDirectory;

    /// <summary>The resolved records directory, or null when it could not be resolved at all.</summary>
    internal string? DiagnosticsDirectory => _diagnosticsDirectory;

    /// <summary>
    /// Builds a sink rooted at an explicit data directory. Used by tests and by callers that
    /// already know the directory.
    /// </summary>
    internal static CliFailureSink ForDataDirectory(string? dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            return new CliFailureSink(diagnosticsDirectory: null);
        }

        try
        {
            return new CliFailureSink(Path.GetFullPath(Path.Combine(dataDirectory, DirectoryName)));
        }
        catch (Exception)
        {
            // An unresolvable path must never crash the failure boundary itself.
            return new CliFailureSink(diagnosticsDirectory: null);
        }
    }

    /// <summary>
    /// Builds a sink from a SQLite connection string, using the same data-directory resolution the
    /// CLI first-run bootstrap uses. When the data source cannot be resolved to a path (for example
    /// <c>:memory:</c>) that resolution falls back to the current working directory, exactly as
    /// <see cref="CliFirstRunBootstrapper"/> does for <c>appsettings.local.json</c>.
    /// </summary>
    internal static CliFailureSink ForConnectionString(string? connectionString) =>
        ForDataDirectory(CliFirstRunBootstrapper.ResolveDataDirectory(
            string.IsNullOrWhiteSpace(connectionString) ? "Data Source=taskdeck.db" : connectionString));

    /// <summary>
    /// Builds a sink from the environment alone, before any host or configuration is built, so a
    /// failure thrown during the host build still has a sink. Mirrors the configuration inputs
    /// <c>Program.cs</c> reads: the standard <c>ConnectionStrings:DefaultConnection</c> environment
    /// spellings (plain and <c>TASKDECK_</c>-prefixed) and then <c>TASKDECK_CONNECTION_STRING</c>,
    /// falling back to the same <c>Data Source=taskdeck.db</c> default.
    /// </summary>
    internal static CliFailureSink FromEnvironment()
    {
        string? connectionString = null;
        try
        {
            connectionString = FirstNonEmpty(
                Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection"),
                Environment.GetEnvironmentVariable("TASKDECK_ConnectionStrings__DefaultConnection"),
                Environment.GetEnvironmentVariable("TASKDECK_CONNECTION_STRING"));
        }
        catch (Exception)
        {
            // A restricted host can refuse environment reads; fall through to the default.
        }

        return ForConnectionString(connectionString);
    }

    /// <summary>
    /// Generates the short reference shown to the operator when no harness trace correlation
    /// exists: 12 lowercase hex characters from a cryptographic RNG, so two records never collide
    /// inside one timestamp second.
    /// </summary>
    internal static string CreateReference() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(ReferenceLength / 2)).ToLowerInvariant();

    /// <summary>
    /// Writes exactly one bounded record for this failure and returns whether it was kept.
    /// Returns false on any IO or permission failure so the caller can say diagnostics were not
    /// captured; it never throws and never emits output.
    /// </summary>
    internal bool TryRecord(Exception exception, string reference, IReadOnlyList<string>? arguments) =>
        TryRecord(exception, reference, arguments, DateTimeOffset.UtcNow);

    /// <summary>
    /// Timestamp-injectable overload so tests can pin the record path deterministically.
    /// </summary>
    internal bool TryRecord(
        Exception exception,
        string reference,
        IReadOnlyList<string>? arguments,
        DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (_diagnosticsDirectory is null || string.IsNullOrWhiteSpace(reference))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(_diagnosticsDirectory);
            EvictOldestRecords(_diagnosticsDirectory);

            var path = Path.Combine(_diagnosticsDirectory, BuildFileName(reference, timestamp));

            var payload = BuildPayload(exception, reference, arguments, timestamp);

            var options = new FileStreamOptions
            {
                // Never append to, and never follow, whatever already sits at the target path: a
                // stale record or a planted symlink must make this write fail, not redirect it.
                // CreateNew maps to O_CREAT|O_EXCL on POSIX, which refuses an existing symlink
                // (dangling included) rather than opening the file it points at.
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None
            };

            if (!OperatingSystem.IsWindows())
            {
                // Set the mode at creation, not afterwards: on a default POSIX umask (022) a
                // create-then-chmod leaves a window in which the record is world-readable.
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using var stream = new FileStream(path, options);
            stream.Write(payload);
            stream.Flush();
            return true;
        }
        catch (Exception)
        {
            // Fail open. An unwritable directory, a full disk, a pre-existing target or a
            // permission error must not change the command result, the exit code, or the output.
            return false;
        }
    }

    /// <summary>
    /// Record file name: the UTC timestamp prefix makes ordinal name order chronological, and the
    /// reference ties the file to the line the operator saw on stderr.
    /// </summary>
    internal static string BuildFileName(string reference, DateTimeOffset timestamp) =>
        $"{FileNamePrefix}{timestamp.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)}-{reference}{FileNameExtension}";

    /// <summary>
    /// Builds the record bytes, truncated to <see cref="MaximumRecordBytes"/> with an explicit
    /// marker so a reader can tell a bounded record from a complete one.
    /// </summary>
    internal static byte[] BuildPayload(
        Exception exception,
        string reference,
        IReadOnlyList<string>? arguments,
        DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var builder = new StringBuilder();
        builder.Append("taskdeck-cli-failure v1\n");
        builder.Append($"timestamp: {timestamp.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)}\n");
        builder.Append($"correlation: {reference}\n");
        builder.Append($"version: {DescribeVersion()}\n");
        builder.Append($"argv: {DescribeArguments(arguments)}\n");
        builder.Append($"exception: {SensitiveDataRedactor.SummarizeException(exception)}\n");

        return Bound(builder.ToString());
    }

    private static byte[] Bound(string content)
    {
        var bytes = StrictUtf8.GetBytes(content);
        if (bytes.Length <= MaximumRecordBytes)
        {
            return bytes;
        }

        var marker = StrictUtf8.GetBytes(TruncationMarker);
        var keep = MaximumRecordBytes - marker.Length;

        // Never split a multi-byte UTF-8 sequence: back off over continuation bytes.
        while (keep > 0 && (bytes[keep] & 0xC0) == 0x80)
        {
            keep--;
        }

        var bounded = new byte[keep + marker.Length];
        Array.Copy(bytes, bounded, keep);
        Array.Copy(marker, 0, bounded, keep, marker.Length);
        return bounded;
    }

    /// <summary>
    /// Deletes the oldest records, by name, until writing one more stays within
    /// <see cref="MaximumRecordCount"/>. The timestamp prefix makes ordinal name order the same as
    /// chronological order.
    /// </summary>
    private static void EvictOldestRecords(string diagnosticsDirectory)
    {
        var existing = Directory.GetFiles(diagnosticsDirectory, FileNameSearchPattern);
        if (existing.Length < MaximumRecordCount)
        {
            return;
        }

        Array.Sort(existing, StringComparer.Ordinal);
        var surplus = existing.Length - MaximumRecordCount + 1;
        for (var index = 0; index < surplus; index++)
        {
            try
            {
                File.Delete(existing[index]);
            }
            catch (Exception)
            {
                // A record another process holds open must not stop this one being written.
            }
        }
    }

    private static string DescribeVersion()
    {
        try
        {
            var version = ProductVersion.Value;
            return string.IsNullOrWhiteSpace(version) ? "unknown" : version;
        }
        catch (Exception)
        {
            return "unknown";
        }
    }

    private static string DescribeArguments(IReadOnlyList<string>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return "(none)";
        }

        var joined = string.Join(' ', arguments);
        var redacted = SensitiveDataRedactor.Redact(joined);
        return string.IsNullOrWhiteSpace(redacted) ? "(none)" : redacted;
    }

    private static string? FirstNonEmpty(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
