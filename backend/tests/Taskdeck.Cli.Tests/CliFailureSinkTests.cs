using System.Globalization;
using System.Text;
using FluentAssertions;
using Taskdeck.Application.Services;
using Taskdeck.Cli.Commands;
using Xunit;

namespace Taskdeck.Cli.Tests;

/// <summary>
/// #2468: the always-on CLI diagnostic sink. It must keep one bounded, redacted record per
/// unexpected failure under the data directory, evict oldest-first at its record cap, refuse to
/// touch anything already sitting at the target path, and fail open on every IO error.
/// </summary>
public sealed class CliFailureSinkTests
{
    private const string SecretToken = "sk-live-ABC123";
    private const string WindowsPath = @"C:\Users\operator\AppData\Local\Taskdeck\taskdeck.db";
    private const string SqliteConstraint =
        "SQLite Error 19: 'UNIQUE constraint failed: Cards.BoardId, Cards.Title'";
    private const string ProviderUrl = "https://api.openai.example/v1/chat/completions";
    private const string Reference = "0a1b2c3d4e5f";
    private static readonly DateTimeOffset FixedTimestamp =
        new(2026, 9, 4, 11, 22, 33, TimeSpan.Zero);

    private static Exception CreateLeakyException() =>
        new InvalidOperationException(
            $"Persisting card failed against {WindowsPath}: {SqliteConstraint}",
            new HttpRequestException(
                $"POST {ProviderUrl} rejected api_key={SecretToken}"));

    [Fact]
    public void TryRecord_WritesOneRedactedRecordUnderTheDataDirectory()
    {
        using var directory = new TemporaryDirectory();
        var sink = CliFailureSink.ForDataDirectory(directory.Path);

        var captured = sink.TryRecord(
            CreateLeakyException(),
            Reference,
            new[] { "boards", "list", $"--token={SecretToken}" },
            FixedTimestamp);

        captured.Should().BeTrue();
        var records = Directory.GetFiles(
            Path.Combine(directory.Path, CliFailureSink.DirectoryName),
            CliFailureSink.FileNameSearchPattern);
        records.Should().HaveCount(1);
        Path.GetFileName(records[0]).Should().Be("cli-failure-20260904T112233Z-0a1b2c3d4e5f.txt");

        var content = File.ReadAllText(records[0]);
        content.Should().Contain($"correlation: {Reference}");
        content.Should().Contain("timestamp: 2026-09-04T11:22:33Z");
        content.Should().Contain("exception: InvalidOperationException:");
        // The argv token is redacted, so the operator's secret is never retained in raw form.
        content.Should().NotContain(SecretToken);
        content.Should().Contain($"--token={SensitiveDataRedactor.RedactedValue}");
        // The record carries the bounded summary, never a stack trace.
        content.Should().NotContain("   at ");
    }

    [Fact]
    public void TryRecord_DoesNotTouchAFileAlreadyAtTheTargetPath()
    {
        using var directory = new TemporaryDirectory();
        var diagnostics = Path.Combine(directory.Path, CliFailureSink.DirectoryName);
        Directory.CreateDirectory(diagnostics);
        var targetPath = Path.Combine(
            diagnostics,
            CliFailureSink.BuildFileName(Reference, FixedTimestamp));
        const string planted = "planted-content";
        File.WriteAllText(targetPath, planted);

        var sink = CliFailureSink.ForDataDirectory(directory.Path);
        var captured = sink.TryRecord(CreateLeakyException(), Reference, arguments: null, FixedTimestamp);

        captured.Should().BeFalse();
        File.ReadAllText(targetPath).Should().Be(planted);
    }

    [Fact]
    public void TryRecord_EvictsTheOldestRecordsAtTheRetentionCap()
    {
        using var directory = new TemporaryDirectory();
        var diagnostics = Path.Combine(directory.Path, CliFailureSink.DirectoryName);
        Directory.CreateDirectory(diagnostics);

        // Five more than the cap, named so ordinal order is chronological order.
        var seeded = new List<string>();
        for (var index = 0; index < CliFailureSink.MaximumRecordCount + 5; index++)
        {
            var name = CliFailureSink.BuildFileName(
                "aaaaaaaaaaaa",
                FixedTimestamp.AddSeconds(index));
            var path = Path.Combine(diagnostics, name);
            File.WriteAllText(path, "seed");
            seeded.Add(name);
        }

        var sink = CliFailureSink.ForDataDirectory(directory.Path);
        var captured = sink.TryRecord(
            CreateLeakyException(),
            Reference,
            arguments: null,
            FixedTimestamp.AddDays(1));

        captured.Should().BeTrue();
        var remaining = Directory
            .GetFiles(diagnostics, CliFailureSink.FileNameSearchPattern)
            .Select(Path.GetFileName)
            .ToArray();
        remaining.Should().HaveCount(CliFailureSink.MaximumRecordCount);
        remaining.Should().Contain(CliFailureSink.BuildFileName(Reference, FixedTimestamp.AddDays(1)));
        // The six oldest went; the newest seeded ones stayed.
        remaining.Should().NotContain(seeded[0]);
        remaining.Should().NotContain(seeded[5]);
        remaining.Should().Contain(seeded[6]);
        remaining.Should().Contain(seeded[^1]);
    }

    [Fact]
    public void TryRecord_TruncatesAnOversizedRecordWithAMarker()
    {
        using var directory = new TemporaryDirectory();
        var sink = CliFailureSink.ForDataDirectory(directory.Path);

        // argv is the only unbounded input: the exception summary is already capped by the
        // redactor at five levels and 1024 characters.
        var hugeArguments = Enumerable.Range(0, 400)
            .Select(index => $"--flag{index.ToString(CultureInfo.InvariantCulture)}=" + new string('x', 40))
            .ToArray();

        var captured = sink.TryRecord(CreateLeakyException(), Reference, hugeArguments, FixedTimestamp);

        captured.Should().BeTrue();
        var path = Path.Combine(
            directory.Path,
            CliFailureSink.DirectoryName,
            CliFailureSink.BuildFileName(Reference, FixedTimestamp));
        var bytes = File.ReadAllBytes(path);
        bytes.Length.Should().Be(CliFailureSink.MaximumRecordBytes);
        Encoding.UTF8.GetString(bytes).Should().EndWith(CliFailureSink.TruncationMarker);
    }

    [Fact]
    public void TryRecord_FailsOpenWhenTheDiagnosticsDirectoryCannotBeCreated()
    {
        using var directory = new TemporaryDirectory();
        // A plain file occupying the directory name makes Directory.CreateDirectory fail.
        File.WriteAllText(Path.Combine(directory.Path, CliFailureSink.DirectoryName), "blocker");
        var sink = CliFailureSink.ForDataDirectory(directory.Path);

        sink.TryRecord(CreateLeakyException(), Reference, arguments: null, FixedTimestamp)
            .Should().BeFalse();
    }

    [Fact]
    public void Handle_WhenTheSinkCannotWrite_SaysDiagnosticsWereNotCapturedAndLeaksNothing()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, CliFailureSink.DirectoryName), "blocker");
        var sink = CliFailureSink.ForDataDirectory(directory.Path);
        using var stderr = new StringWriter();

        var exitCode = CliUnexpectedFailure.Handle(
            CreateLeakyException(),
            trace: null,
            stderr,
            sink,
            new[] { "boards", "list" });

        exitCode.Should().Be(ExitCodes.Failure);
        var output = stderr.ToString();
        output.Should().Contain(CliUnexpectedFailure.DiagnosticsUnavailableNotice);
        output.Should().Contain(SensitiveDataRedactor.GenericUnexpectedFailureMessage);
        output.Should().NotContain("(trace correlation:");
        foreach (var fragment in new[] { SecretToken, WindowsPath, SqliteConstraint, ProviderUrl })
        {
            output.Should().NotContain(fragment);
        }

        output.Should().NotContain("InvalidOperationException");
        output.Should().NotContain("   at ");
    }

    [Fact]
    public void Handle_WithASink_PrintsTheSameReferenceTheRecordIsFiledUnder()
    {
        using var directory = new TemporaryDirectory();
        var sink = CliFailureSink.ForDataDirectory(directory.Path);
        using var stderr = new StringWriter();

        var exitCode = CliUnexpectedFailure.Handle(
            CreateLeakyException(),
            trace: null,
            stderr,
            sink,
            new[] { "boards", "list" });

        exitCode.Should().Be(ExitCodes.Failure);
        var output = stderr.ToString();
        output.Should().NotContain(CliUnexpectedFailure.DiagnosticsUnavailableNotice);

        var records = Directory.GetFiles(
            Path.Combine(directory.Path, CliFailureSink.DirectoryName),
            CliFailureSink.FileNameSearchPattern);
        records.Should().HaveCount(1);

        const string marker = "(trace correlation: ";
        var start = output.IndexOf(marker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        start += marker.Length;
        var reference = output[start..output.IndexOf(')', start)];
        reference.Should().MatchRegex("^[0-9a-f]{12}$");
        Path.GetFileName(records[0]).Should().EndWith($"-{reference}.txt");
    }

    [Fact]
    public void CreateReference_IsTwelveLowercaseHexCharactersAndUnique()
    {
        var references = Enumerable.Range(0, 50).Select(_ => CliFailureSink.CreateReference()).ToArray();

        references.Should().OnlyContain(reference => reference.Length == CliFailureSink.ReferenceLength);
        references.Should().OnlyContain(reference => reference.All(Uri.IsHexDigit));
        references.Should().OnlyContain(reference => reference == reference.ToLowerInvariant());
        references.Distinct().Should().HaveCount(references.Length);
    }

    [Fact]
    public void Record_IsOwnerReadWriteOnly_OnPosix()
    {
        if (OperatingSystem.IsWindows())
        {
            // Skipped on Windows: there is no Unix mode to assert; NTFS ACL inheritance governs
            // access there. The Linux CI leg is what proves the 0600 creation mode.
            return;
        }

        using var directory = new TemporaryDirectory();
        var sink = CliFailureSink.ForDataDirectory(directory.Path);

        sink.TryRecord(CreateLeakyException(), Reference, arguments: null, FixedTimestamp)
            .Should().BeTrue();

        var path = Path.Combine(
            directory.Path,
            CliFailureSink.DirectoryName,
            CliFailureSink.BuildFileName(Reference, FixedTimestamp));
        File.GetUnixFileMode(path).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [Fact]
    public void ForConnectionString_PutsRecordsBesideTheSqliteDatabase()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = Path.Combine(directory.Path, "taskdeck.db");

        var sink = CliFailureSink.ForConnectionString($"Data Source={databasePath}");

        sink.DiagnosticsDirectory.Should().Be(
            Path.GetFullPath(Path.Combine(directory.Path, CliFailureSink.DirectoryName)));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "taskdeck-cli-sink-" + Guid.NewGuid().ToString("N"));
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
