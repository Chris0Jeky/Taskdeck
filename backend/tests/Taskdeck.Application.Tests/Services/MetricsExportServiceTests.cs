using FluentAssertions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class MetricsExportServiceTests
{
    private static BoardMetricsResponse CreateSampleMetrics(
        Guid? boardId = null,
        IReadOnlyList<ThroughputDataPoint>? throughput = null,
        IReadOnlyList<CycleTimeEntry>? cycleTime = null,
        IReadOnlyList<WipSnapshot>? wip = null,
        IReadOnlyList<BlockedCardSummary>? blocked = null)
    {
        var bid = boardId ?? Guid.NewGuid();
        return new BoardMetricsResponse(
            bid,
            DateTimeOffset.UtcNow.AddDays(-30),
            DateTimeOffset.UtcNow,
            throughput ?? Array.Empty<ThroughputDataPoint>(),
            2.5,
            cycleTime ?? Array.Empty<CycleTimeEntry>(),
            wip ?? Array.Empty<WipSnapshot>(),
            10,
            0,
            blocked ?? Array.Empty<BlockedCardSummary>());
    }

    [Fact]
    public void BuildCsv_ShouldIncludeSchemaVersionHeader()
    {
        var metrics = CreateSampleMetrics();
        var csv = MetricsExportService.BuildCsv(metrics);

        csv.Should().Contain($"# schema_version={MetricsExportService.SchemaVersion}");
    }

    [Fact]
    public void BuildCsv_ShouldIncludeBoardIdHeader()
    {
        var boardId = Guid.NewGuid();
        var metrics = CreateSampleMetrics(boardId: boardId);
        var csv = MetricsExportService.BuildCsv(metrics);

        csv.Should().Contain($"# board_id={boardId}");
    }

    [Fact]
    public void BuildCsv_ShouldIncludeAllSections()
    {
        var metrics = CreateSampleMetrics();
        var csv = MetricsExportService.BuildCsv(metrics);

        csv.Should().Contain("[Summary]");
        csv.Should().Contain("[Throughput]");
        csv.Should().Contain("[CycleTime]");
        csv.Should().Contain("[WIP]");
        csv.Should().Contain("[Blocked]");
    }

    [Fact]
    public void BuildCsv_ShouldIncludeSummaryValues()
    {
        var metrics = CreateSampleMetrics();
        var csv = MetricsExportService.BuildCsv(metrics);

        csv.Should().Contain("AverageCycleTimeDays,2.5");
        csv.Should().Contain("TotalWip,10");
        csv.Should().Contain("BlockedCount,0");
        csv.Should().Contain("TotalThroughput,0");
    }

    [Fact]
    public void BuildCsv_ShouldIncludeThroughputData()
    {
        var throughput = new[]
        {
            new ThroughputDataPoint(new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero), 3),
            new ThroughputDataPoint(new DateTimeOffset(2026, 4, 2, 0, 0, 0, TimeSpan.Zero), 5),
        };

        var metrics = CreateSampleMetrics(throughput: throughput);
        var csv = MetricsExportService.BuildCsv(metrics);

        csv.Should().Contain("Date,CompletedCount");
        csv.Should().Contain("2026-04-01,3");
        csv.Should().Contain("2026-04-02,5");
        csv.Should().Contain("TotalThroughput,8");
    }

    [Fact]
    public void BuildCsv_ShouldIncludeCycleTimeEntries()
    {
        var cardId = Guid.NewGuid();
        var cycleTime = new[] { new CycleTimeEntry(cardId, "Test Card", 3.5) };
        var metrics = CreateSampleMetrics(cycleTime: cycleTime);
        var csv = MetricsExportService.BuildCsv(metrics);

        csv.Should().Contain("CardId,CardTitle,CycleTimeDays");
        csv.Should().Contain($"{cardId},Test Card,3.5");
    }

    [Fact]
    public void BuildCsv_ShouldIncludeWipSnapshots()
    {
        var colId = Guid.NewGuid();
        var wip = new[] { new WipSnapshot(colId, "In Progress", 5, 8) };
        var metrics = CreateSampleMetrics(wip: wip);
        var csv = MetricsExportService.BuildCsv(metrics);

        csv.Should().Contain("ColumnId,ColumnName,CardCount,WipLimit");
        csv.Should().Contain($"{colId},In Progress,5,8");
    }

    [Fact]
    public void BuildCsv_ShouldIncludeBlockedCards()
    {
        var cardId = Guid.NewGuid();
        var blocked = new[] { new BlockedCardSummary(cardId, "Blocked Card", "Waiting on API", 2.3) };
        var metrics = CreateSampleMetrics(blocked: blocked);
        var csv = MetricsExportService.BuildCsv(metrics);

        csv.Should().Contain("CardId,CardTitle,BlockReason,BlockedDurationDays");
        csv.Should().Contain($"{cardId},Blocked Card,Waiting on API,2.3");
    }

    [Fact]
    public void BuildCsv_EmptyMetrics_ShouldReturnValidCsvWithHeaders()
    {
        var metrics = CreateSampleMetrics();
        var csv = MetricsExportService.BuildCsv(metrics);

        csv.Should().NotBeNullOrWhiteSpace();
        csv.Should().Contain("[Summary]");
        csv.Should().Contain("TotalThroughput,0");
    }

    // --- CSV Injection Protection Tests ---

    [Theory]
    [InlineData("=SUM(A1:A10)", "SUM(A1:A10)")]
    [InlineData("+cmd|'/C calc'!A0", "cmd|'/C calc'!A0")]
    [InlineData("-1+2", "1+2")]
    [InlineData("@SUM(1+1)*cmd|'/C calc'!A0", "SUM(1+1)*cmd|'/C calc'!A0")]
    [InlineData("\tcmd", "cmd")]
    [InlineData("\rcmd", "cmd")]
    public void SanitizeCsvField_ShouldStripDangerousLeadingCharacters(string input, string expected)
    {
        MetricsExportService.SanitizeCsvField(input).Should().Be(expected);
    }

    [Fact]
    public void SanitizeCsvField_ShouldQuoteFieldWithComma()
    {
        MetricsExportService.SanitizeCsvField("hello, world").Should().Be("\"hello, world\"");
    }

    [Fact]
    public void SanitizeCsvField_ShouldDoubleQuotesInField()
    {
        MetricsExportService.SanitizeCsvField("say \"hello\"").Should().Be("\"say \"\"hello\"\"\"");
    }

    [Fact]
    public void SanitizeCsvField_ShouldQuoteFieldWithNewline()
    {
        MetricsExportService.SanitizeCsvField("line1\nline2").Should().Be("\"line1\nline2\"");
    }

    [Fact]
    public void SanitizeCsvField_ShouldHandleEmptyString()
    {
        MetricsExportService.SanitizeCsvField("").Should().Be("");
    }

    [Fact]
    public void SanitizeCsvField_ShouldHandleNormalText()
    {
        MetricsExportService.SanitizeCsvField("Normal card title").Should().Be("Normal card title");
    }

    [Fact]
    public void SanitizeCsvField_ShouldStripMultipleDangerousLeadingChars()
    {
        MetricsExportService.SanitizeCsvField("==+cmd").Should().Be("cmd");
    }

    [Theory]
    [InlineData("hello\n=CMD|'/C calc'!A0", "\"hello\nCMD|'/C calc'!A0\"")]
    [InlineData("line1\n+cmd\nline3", "\"line1\ncmd\nline3\"")]
    [InlineData("safe\nsafe2", "\"safe\nsafe2\"")]
    [InlineData("ok\n@evil", "\"ok\nevil\"")]
    [InlineData("a\n\t\r=bad", "\"a\nbad\"")]
    public void SanitizeCsvField_ShouldStripDangerousCharsFromEmbeddedLines(string input, string expected)
    {
        MetricsExportService.SanitizeCsvField(input).Should().Be(expected);
    }
}
