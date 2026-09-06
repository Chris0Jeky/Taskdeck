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
        // redactor at five levels and 1024 characters. Values are replaced, so the unbounded part
        // is what the policy still retains — the flag names themselves.
        var hugeArguments = Enumerable.Range(0, 400)
            .Select(index => $"--flag{index.ToString(CultureInfo.InvariantCulture)}-" + new string('x', 40))
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

    /// <summary>
    /// The sink is constructed before the CLI's unknown-exception boundary exists, so it must
    /// swallow every parse failure of the operator-supplied connection string. A non-boolean
    /// keyword value makes <c>SqliteConnectionStringBuilder</c> raise a <see cref="FormatException"/>,
    /// which none of the filters in the data-directory resolution chain catch.
    /// </summary>
    [Theory]
    [InlineData("Data Source=taskdeck.db;Foreign Keys=yes")]
    [InlineData("Data Source=taskdeck.db;Default Timeout=abc")]
    [InlineData("Data Source=taskdeck.db;Pooling=sometimes")]
    public void ForConnectionString_WithAnUnparsableKeywordValue_FallsBackInsteadOfThrowing(
        string connectionString)
    {
        var sink = CliFailureSink.ForConnectionString(connectionString);

        // Same current-directory root the resolver uses for any other unresolvable data source.
        sink.DiagnosticsDirectory.Should().Be(Path.GetFullPath(
            Path.Combine(Directory.GetCurrentDirectory(), CliFailureSink.DirectoryName)));
    }

    /// <summary>
    /// #2577 item 1: eviction must run only after the new record is on disk. A stale file at the
    /// exact target name makes the CreateNew write fail, and a failed write must delete nothing,
    /// otherwise the failure mode is net diagnostic loss instead of fail-open-with-no-change.
    /// </summary>
    [Fact]
    public void TryRecord_WhenTheWriteFailsAtTheCap_DeletesNoOlderRecord()
    {
        using var directory = new TemporaryDirectory();
        var diagnostics = Path.Combine(directory.Path, CliFailureSink.DirectoryName);
        Directory.CreateDirectory(diagnostics);

        // Exactly the cap: one stale file planted at the target name, plus older records that a
        // pre-write eviction would delete to make room for a write that then cannot happen.
        var targetName = CliFailureSink.BuildFileName(Reference, FixedTimestamp);
        const string planted = "planted-content";
        File.WriteAllText(Path.Combine(diagnostics, targetName), planted);

        var seeded = new List<string> { targetName };
        for (var index = 1; index < CliFailureSink.MaximumRecordCount; index++)
        {
            var name = CliFailureSink.BuildFileName(
                "aaaaaaaaaaaa",
                FixedTimestamp.AddSeconds(index - CliFailureSink.MaximumRecordCount));
            File.WriteAllText(Path.Combine(diagnostics, name), "seed");
            seeded.Add(name);
        }

        var sink = CliFailureSink.ForDataDirectory(directory.Path);
        var captured = sink.TryRecord(CreateLeakyException(), Reference, arguments: null, FixedTimestamp);

        captured.Should().BeFalse();
        var remaining = Directory
            .GetFiles(diagnostics, CliFailureSink.FileNameSearchPattern)
            .Select(Path.GetFileName)
            .ToArray();
        remaining.Should().BeEquivalentTo(seeded);
        File.ReadAllText(Path.Combine(diagnostics, targetName)).Should().Be(planted);
    }

    /// <summary>
    /// #2577 item 2: the reference is interpolated into the record file name, so TryRecord checks
    /// its shape itself instead of trusting the callers to keep the invariant. Only hex of exactly
    /// 12 characters (the generated reference) or 32 (the harness trace correlation) is accepted;
    /// anything else fails open, writing nothing anywhere under the data directory.
    /// </summary>
    [Theory]
    [InlineData("../x")]
    [InlineData("a/../../x")]
    [InlineData("0a1b2c3d4e5")]
    [InlineData("0a1b2c3d4e5f0")]
    [InlineData("zzzzzzzzzzzz")]
    [InlineData("")]
    [InlineData("   ")]
    public void TryRecord_WithAReferenceThatIsNotAnAcceptedCorrelation_FailsOpenAndWritesNothing(
        string reference)
    {
        using var directory = new TemporaryDirectory();
        var sink = CliFailureSink.ForDataDirectory(directory.Path);

        var captured = sink.TryRecord(CreateLeakyException(), reference, arguments: null, FixedTimestamp);

        captured.Should().BeFalse();
        Directory.GetFiles(directory.Path, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    /// <summary>
    /// The 32-character lowercase-hex harness trace correlation stays accepted: it is the
    /// reference a harness run's record is filed under.
    /// </summary>
    [Fact]
    public void TryRecord_AcceptsTheThirtyTwoCharacterHarnessCorrelation()
    {
        using var directory = new TemporaryDirectory();
        var sink = CliFailureSink.ForDataDirectory(directory.Path);
        var correlation = Guid.NewGuid().ToString("N");

        sink.TryRecord(CreateLeakyException(), correlation, arguments: null, FixedTimestamp)
            .Should().BeTrue();

        File.Exists(Path.Combine(
            directory.Path,
            CliFailureSink.DirectoryName,
            CliFailureSink.BuildFileName(correlation, FixedTimestamp))).Should().BeTrue();
    }

    /// <summary>
    /// #2577 item 3: the record keeps the command grammar (the group, the command and the flag
    /// names) and replaces every other argv token with a fixed placeholder, so neither a
    /// space-separated secret nor ordinary user content such as a card title reaches disk.
    /// </summary>
    [Fact]
    public void TryRecord_KeepsCommandAndFlagNamesButNoArgumentValues()
    {
        using var directory = new TemporaryDirectory();
        var sink = CliFailureSink.ForDataDirectory(directory.Path);

        var captured = sink.TryRecord(
            CreateLeakyException(),
            Reference,
            new[] { "cards", "add", "--title", "Secret plan", "--token", "abc123" },
            FixedTimestamp);

        captured.Should().BeTrue();
        var content = File.ReadAllText(Path.Combine(
            directory.Path,
            CliFailureSink.DirectoryName,
            CliFailureSink.BuildFileName(Reference, FixedTimestamp)));

        content.Should().Contain("argv: cards add --title [value] --token [value]");
        content.Should().NotContain("Secret plan");
        content.Should().NotContain("abc123");
    }

    /// <summary>
    /// Eviction runs after the stream is closed, so the record is already durable by then: an
    /// enumeration failure there must not be reported as a capture failure, or the CLI prints the
    /// "diagnostics were not captured" notice for a record that exists on disk.
    /// </summary>
    [Fact]
    public void TryRecord_WhenEvictionFails_StillReportsTheAlreadyWrittenRecordAsKept()
    {
        using var directory = new TemporaryDirectory();
        var sink = CliFailureSink.ForDataDirectory(
            directory.Path,
            listRecords: (_, _) => throw new IOException("record enumeration failed"));

        var captured = sink.TryRecord(CreateLeakyException(), Reference, arguments: null, FixedTimestamp);

        captured.Should().BeTrue();
        var path = Path.Combine(
            directory.Path,
            CliFailureSink.DirectoryName,
            CliFailureSink.BuildFileName(Reference, FixedTimestamp));
        File.Exists(path).Should().BeTrue();
        File.ReadAllText(path).Should().Contain($"correlation: {Reference}");
    }

    /// <summary>
    /// A record must never evict itself. When the record just written is the oldest name at the
    /// cap, the surplus has to come out of the next-oldest instead.
    /// </summary>
    [Fact]
    public void TryRecord_WhenTheNewRecordIsTheOldestAtTheCap_EvictsAnotherAndKeepsItself()
    {
        using var directory = new TemporaryDirectory();
        var diagnostics = Path.Combine(directory.Path, CliFailureSink.DirectoryName);
        Directory.CreateDirectory(diagnostics);

        // Exactly the cap, all newer than the record about to be written, so ordinal name order
        // puts the new record first and the skip branch is the only thing that can save it.
        var seeded = new List<string>();
        for (var index = 1; index <= CliFailureSink.MaximumRecordCount; index++)
        {
            var name = CliFailureSink.BuildFileName(
                "aaaaaaaaaaaa",
                FixedTimestamp.AddSeconds(index));
            File.WriteAllText(Path.Combine(diagnostics, name), "seed");
            seeded.Add(name);
        }

        var sink = CliFailureSink.ForDataDirectory(directory.Path);
        var captured = sink.TryRecord(CreateLeakyException(), Reference, arguments: null, FixedTimestamp);

        captured.Should().BeTrue();
        var remaining = Directory
            .GetFiles(diagnostics, CliFailureSink.FileNameSearchPattern)
            .Select(Path.GetFileName)
            .ToArray();
        remaining.Should().HaveCount(CliFailureSink.MaximumRecordCount);
        remaining.Should().Contain(CliFailureSink.BuildFileName(Reference, FixedTimestamp));
        // The oldest seeded record went instead of the new one.
        remaining.Should().NotContain(seeded[0]);
        remaining.Should().Contain(seeded[1]);
        remaining.Should().Contain(seeded[^1]);
    }

    /// <summary>
    /// The sink must not refuse a reference the CLI itself printed: <c>CliStartupTrace</c> accepts
    /// hex in either case, so the sink has to as well.
    /// </summary>
    [Fact]
    public void TryRecord_AcceptsEveryCorrelationTheStartupTraceAccepts()
    {
        using var directory = new TemporaryDirectory();
        var sink = CliFailureSink.ForDataDirectory(directory.Path);

        var correlations = new[]
        {
            Guid.NewGuid().ToString("N"),
            Guid.NewGuid().ToString("N").ToUpperInvariant(),
            new string('a', CliStartupTrace.CorrelationLength),
            new string('F', CliStartupTrace.CorrelationLength)
        };

        for (var index = 0; index < correlations.Length; index++)
        {
            var correlation = correlations[index];
            CliStartupTrace.IsCorrelationId(correlation).Should().BeTrue();

            var timestamp = FixedTimestamp.AddSeconds(index);
            sink.TryRecord(CreateLeakyException(), correlation, arguments: null, timestamp)
                .Should().BeTrue();
            File.Exists(Path.Combine(
                directory.Path,
                CliFailureSink.DirectoryName,
                CliFailureSink.BuildFileName(correlation, timestamp))).Should().BeTrue();
        }
    }

    /// <summary>The generated 12-character reference shape is accepted in either case too.</summary>
    [Theory]
    [InlineData("0A1B2C3D4E5F")]
    [InlineData("0a1B2c3D4e5F")]
    public void TryRecord_AcceptsAGeneratedReferenceInEitherCase(string reference)
    {
        using var directory = new TemporaryDirectory();
        var sink = CliFailureSink.ForDataDirectory(directory.Path);

        sink.TryRecord(CreateLeakyException(), reference, arguments: null, FixedTimestamp)
            .Should().BeTrue();

        File.Exists(Path.Combine(
            directory.Path,
            CliFailureSink.DirectoryName,
            CliFailureSink.BuildFileName(reference, FixedTimestamp))).Should().BeTrue();
    }

    /// <summary>
    /// #2577 follow-up: an attached value must go the same way as a separate one, whether or not
    /// its flag name is one the redactor knows. Only the flag name survives.
    /// </summary>
    [Fact]
    public void TryRecord_ReplacesADashLeadingValueThatContainsWhitespace()
    {
        using var directory = new TemporaryDirectory();
        var sink = CliFailureSink.ForDataDirectory(directory.Path);

        var captured = sink.TryRecord(
            CreateLeakyException(),
            Reference,
            new[] { "cards", "add", "--title", "- fix login for jane@acme.com", "--board", "b1" },
            FixedTimestamp);

        captured.Should().BeTrue();
        var content = File.ReadAllText(Path.Combine(
            directory.Path,
            CliFailureSink.DirectoryName,
            CliFailureSink.BuildFileName(Reference, FixedTimestamp)));

        content.Should().Contain("argv: cards add --title [value] --board [value]");
        content.Should().NotContain("fix login");
        content.Should().NotContain("jane@acme.com");
    }

    [Fact]
    public void TryRecord_ReplacesAnAttachedValueEvenWhenTheFlagIsNotASecretKeyword()
    {
        using var directory = new TemporaryDirectory();
        var sink = CliFailureSink.ForDataDirectory(directory.Path);

        var captured = sink.TryRecord(
            CreateLeakyException(),
            Reference,
            new[] { "cards", "add", "--title=Secret plan", "--token=abc123" },
            FixedTimestamp);

        captured.Should().BeTrue();
        var content = File.ReadAllText(Path.Combine(
            directory.Path,
            CliFailureSink.DirectoryName,
            CliFailureSink.BuildFileName(Reference, FixedTimestamp)));

        content.Should().Contain("argv: cards add --title=[value] ");
        content.Should().Contain($"--token={SensitiveDataRedactor.RedactedValue}");
        content.Should().NotContain("Secret plan");
        content.Should().NotContain("abc123");
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
