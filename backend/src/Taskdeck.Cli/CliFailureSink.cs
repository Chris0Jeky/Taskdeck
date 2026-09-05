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
/// and length) and the shape of the command line, never its values: only the leading command words
/// and the flag names survive, every other argument — an attached <c>--flag=value</c> value
/// included — becomes <see cref="ArgumentValuePlaceholder"/>, and the result still goes through
/// <see cref="SensitiveDataRedactor.Redact"/> (#2577).
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

    /// <summary>Length, in hex characters, of a generated correlation reference.</summary>
    internal const int ReferenceLength = 12;

    /// <summary>
    /// Length, in hex characters, of the harness startup-trace correlation, the other reference
    /// shape a caller may file a record under. Taken from <see cref="CliStartupTrace"/> rather
    /// than repeated, so the sink cannot drift from the trace that produced the correlation.
    /// </summary>
    internal const int TraceCorrelationLength = CliStartupTrace.CorrelationLength;

    /// <summary>
    /// Stand-in written in place of every argv token that is not a command word or a flag name
    /// (#2577). The sink retains the shape of the failing command, never the operator's values.
    /// </summary>
    internal const string ArgumentValuePlaceholder = "[value]";

    /// <summary>
    /// Depth of the CLI's command grammar: a group and a command, as in <c>cards add</c>
    /// (see <see cref="Commands.CommandDispatcher"/>). Nothing past the second token is a command
    /// word, so nothing past it may be retained verbatim unless it names a flag.
    /// </summary>
    private const int MaximumCommandWords = 2;

    private static readonly UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly string? _diagnosticsDirectory;
    private readonly Func<string, string, string[]> _listRecords;

    private CliFailureSink(string? diagnosticsDirectory, Func<string, string, string[]>? listRecords = null)
    {
        _diagnosticsDirectory = diagnosticsDirectory;
        _listRecords = listRecords ?? Directory.GetFiles;
    }

    /// <summary>The resolved records directory, or null when it could not be resolved at all.</summary>
    internal string? DiagnosticsDirectory => _diagnosticsDirectory;

    /// <summary>
    /// Builds a sink rooted at an explicit data directory. Used by tests and by callers that
    /// already know the directory.
    /// </summary>
    internal static CliFailureSink ForDataDirectory(string? dataDirectory) =>
        ForDataDirectory(dataDirectory, listRecords: null);

    /// <summary>
    /// Same sink, with the retention enumeration supplied. Test seam only: it exists so a test can
    /// make eviction fail after the record is already written and closed, which is the one path
    /// where an exception must not turn a kept record into a reported capture failure.
    /// </summary>
    internal static CliFailureSink ForDataDirectory(
        string? dataDirectory,
        Func<string, string, string[]>? listRecords)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            return new CliFailureSink(diagnosticsDirectory: null, listRecords);
        }

        try
        {
            return new CliFailureSink(Path.GetFullPath(Path.Combine(dataDirectory, DirectoryName)), listRecords);
        }
        catch (Exception)
        {
            // An unresolvable path must never crash the failure boundary itself.
            return new CliFailureSink(diagnosticsDirectory: null, listRecords);
        }
    }

    /// <summary>
    /// Builds a sink from a SQLite connection string, using the same data-directory resolution the
    /// CLI first-run bootstrap uses. When the data source cannot be resolved to a path (for example
    /// <c>:memory:</c>) that resolution falls back to the current working directory, exactly as
    /// <see cref="CliFirstRunBootstrapper"/> does for <c>appsettings.local.json</c>.
    ///
    /// Resolution parses operator input, and it runs before the CLI's unknown-exception boundary
    /// exists, so every failure is absorbed here. The parse can raise types the resolution chain
    /// does not filter: <c>SqliteConnectionStringBuilder</c> converts the Foreign Keys, Recursive
    /// Triggers, Pooling and Default Timeout keywords with <c>Convert</c>, which raises
    /// <see cref="FormatException"/> or <see cref="OverflowException"/> rather than
    /// <see cref="ArgumentException"/>. A malformed connection string must fail through the
    /// boundary later, never escape as an unhandled exception with a raw stack trace.
    /// </summary>
    internal static CliFailureSink ForConnectionString(string? connectionString)
    {
        try
        {
            return ForDataDirectory(CliFirstRunBootstrapper.ResolveDataDirectory(
                string.IsNullOrWhiteSpace(connectionString) ? "Data Source=taskdeck.db" : connectionString));
        }
        catch (Exception)
        {
            // Same current-directory root the resolver itself falls back to for a data source it
            // cannot turn into a path, so diagnostics still land somewhere for this run.
            return ForDataDirectory(TryGetCurrentDirectory());
        }
    }

    private static string? TryGetCurrentDirectory()
    {
        try
        {
            return Directory.GetCurrentDirectory();
        }
        catch (Exception)
        {
            // A deleted or inaccessible working directory leaves the sink without a root; the
            // caller then reports that diagnostics were not captured.
            return null;
        }
    }

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

        // The reference is interpolated into the record's file name, so its shape is checked here
        // rather than trusted from the callers: anything but the two shapes the CLI produces fails
        // open, which keeps a traversal-shaped or otherwise unexpected reference from steering the
        // write out of the diagnostics directory even if a future caller stops validating it.
        if (_diagnosticsDirectory is null || !IsAcceptedReference(reference))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(_diagnosticsDirectory);

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

            using (var stream = new FileStream(path, options))
            {
                stream.Write(payload);
                stream.Flush();
            }

            // Only now, with the new record closed and on disk, is it safe to trim the directory.
            // Evicting first meant a create that then failed (a stale file at the target name, a
            // full disk, a directory that permits delete but not create) destroyed older records
            // and replaced none of them: net diagnostic loss instead of fail-open-with-no-change.
            //
            // Past this point the write has succeeded, so eviction gets its own catch: retention
            // is best effort and must never downgrade a durable record to a reported failure, or
            // the caller prints the "diagnostics were not captured" notice for a record that
            // exists. The directory then keeps more than the cap until a later run trims it.
            try
            {
                EvictOldestRecords(_diagnosticsDirectory, path);
            }
            catch (Exception)
            {
                // Enumeration or sorting failed; the record itself is already on disk.
            }

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
    /// Deletes the oldest records, by name, until the directory is back within
    /// <see cref="MaximumRecordCount"/>. The timestamp prefix makes ordinal name order the same as
    /// chronological order. Runs after the write, so the record just written is already counted
    /// and is skipped explicitly: a record must never evict itself.
    /// </summary>
    private void EvictOldestRecords(string diagnosticsDirectory, string writtenPath)
    {
        var existing = _listRecords(diagnosticsDirectory, FileNameSearchPattern);
        var surplus = existing.Length - MaximumRecordCount;
        if (surplus <= 0)
        {
            return;
        }

        Array.Sort(existing, StringComparer.Ordinal);
        for (var index = 0; index < existing.Length && surplus > 0; index++)
        {
            if (string.Equals(existing[index], writtenPath, StringComparison.Ordinal))
            {
                continue;
            }

            surplus--;
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

    /// <summary>
    /// The two reference shapes the CLI produces: the 12-character generated reference and the
    /// 32-character harness trace correlation, both hex. Case is accepted either way, because
    /// <see cref="CliStartupTrace.IsCorrelationId"/> does: the sink must never refuse a reference
    /// the CLI itself printed to the operator. Hex cannot contain a directory separator, a drive
    /// letter or a dot, so an accepted reference can only ever name a file inside the diagnostics
    /// directory.
    /// </summary>
    private static bool IsAcceptedReference(string? reference) =>
        reference is not null &&
        reference.Length is ReferenceLength or TraceCorrelationLength &&
        reference.All(Uri.IsHexDigit);

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

    /// <summary>
    /// Renders argv under the retention policy decided in #2577: keep the command grammar, drop
    /// every value. A token is retained verbatim only when it starts with '-' (a flag name) or
    /// when it is one of the leading command words. Everything else becomes
    /// <see cref="ArgumentValuePlaceholder"/>, including the value attached to a flag: a
    /// <c>--flag=value</c> token keeps only <c>--flag=</c>. The whole line still goes through
    /// <see cref="SensitiveDataRedactor.Redact"/> below.
    ///
    /// The redactor only masks the <c>key=value</c> and <c>key: value</c> forms, so a
    /// space-separated secret (<c>--token abc123</c>) would otherwise have been retained verbatim,
    /// and ordinary user content such as a card title or description would have been written to
    /// disk on failure where nothing was retained before this sink existed. The command name and
    /// the flag names are what makes a record actionable; the values are not worth their risk.
    /// </summary>
    private static string DescribeArguments(IReadOnlyList<string>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return "(none)";
        }

        var builder = new StringBuilder();
        var commandWords = 0;
        for (var index = 0; index < arguments.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(' ');
            }

            var argument = arguments[index];
            if (argument.StartsWith('-'))
            {
                // A flag name is retained, but its attached value is a value like any other: the
                // redactor only masks the key=value forms whose key it knows, so --title=... or
                // --description=... would otherwise reach disk verbatim.
                var separator = argument.IndexOf('=');
                if (separator < 0)
                {
                    builder.Append(argument);
                }
                else
                {
                    builder.Append(argument, 0, separator + 1).Append(ArgumentValuePlaceholder);
                }

                // Nothing after the first flag is a command word, so no later bare token may be
                // retained on the strength of its shape alone.
                commandWords = MaximumCommandWords;
            }
            else if (commandWords < MaximumCommandWords && IsCommandWord(argument))
            {
                builder.Append(argument);
                commandWords++;
            }
            else
            {
                builder.Append(ArgumentValuePlaceholder);
            }
        }

        var redacted = SensitiveDataRedactor.Redact(builder.ToString());
        return string.IsNullOrWhiteSpace(redacted) ? "(none)" : redacted;
    }

    /// <summary>
    /// The shape every CLI command group and command has: short, lowercase, no whitespace. A
    /// leading token that is not one (a positional value, a title, anything cased or spaced) is
    /// replaced rather than retained.
    /// </summary>
    private static bool IsCommandWord(string argument) =>
        argument.Length is > 0 and <= 32 &&
        argument.All(character => character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');

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
