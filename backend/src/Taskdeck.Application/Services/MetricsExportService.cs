using System.Globalization;
using System.Text;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

/// <summary>
/// Produces CSV exports of board metrics with schema-versioned columns
/// and CSV-injection-safe cell values.
/// </summary>
public class MetricsExportService : IMetricsExportService
{
    /// <summary>
    /// Schema version embedded as the first comment line in every export.
    /// Bump when column layout changes.
    /// </summary>
    internal const string SchemaVersion = "1.0";

    private readonly IBoardMetricsService _metricsService;

    public MetricsExportService(IBoardMetricsService metricsService)
    {
        _metricsService = metricsService;
    }

    public async Task<Result<MetricsExportResult>> ExportCsvAsync(
        BoardMetricsQuery query,
        Guid actingUserId,
        CancellationToken cancellationToken = default)
    {
        var metricsResult = await _metricsService.GetBoardMetricsAsync(query, actingUserId, cancellationToken);
        if (!metricsResult.IsSuccess)
            return Result.Failure<MetricsExportResult>(metricsResult.ErrorCode, metricsResult.ErrorMessage);

        var metrics = metricsResult.Value;
        var csv = BuildCsv(metrics);

        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var fileName = $"board-metrics-{metrics.BoardId:N}-{timestamp}.csv";

        return Result.Success(new MetricsExportResult(
            Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(),
            fileName,
            "text/csv"));
    }

    internal static string BuildCsv(BoardMetricsResponse metrics)
    {
        var sb = new StringBuilder();

        // Schema version header (comment line)
        sb.AppendLine($"# schema_version={SchemaVersion}");
        sb.AppendLine($"# board_id={metrics.BoardId}");
        sb.AppendLine($"# from={metrics.From:o}");
        sb.AppendLine($"# to={metrics.To:o}");
        sb.AppendLine($"# exported_at={DateTimeOffset.UtcNow:o}");
        sb.AppendLine();

        // Section: Summary
        sb.AppendLine("[Summary]");
        sb.AppendLine("Metric,Value");
        sb.AppendLine($"AverageCycleTimeDays,{metrics.AverageCycleTimeDays.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"TotalWip,{metrics.TotalWip}");
        sb.AppendLine($"BlockedCount,{metrics.BlockedCount}");
        sb.AppendLine($"TotalThroughput,{metrics.Throughput.Sum(t => t.CompletedCount)}");
        sb.AppendLine();

        // Section: Throughput
        sb.AppendLine("[Throughput]");
        sb.AppendLine("Date,CompletedCount");
        foreach (var dp in metrics.Throughput)
        {
            sb.AppendLine($"{dp.Date:yyyy-MM-dd},{dp.CompletedCount}");
        }
        sb.AppendLine();

        // Section: CycleTime
        sb.AppendLine("[CycleTime]");
        sb.AppendLine("CardId,CardTitle,CycleTimeDays");
        foreach (var entry in metrics.CycleTimeEntries)
        {
            sb.AppendLine($"{entry.CardId},{SanitizeCsvField(entry.CardTitle)},{entry.CycleTimeDays.ToString(CultureInfo.InvariantCulture)}");
        }
        sb.AppendLine();

        // Section: WIP
        sb.AppendLine("[WIP]");
        sb.AppendLine("ColumnId,ColumnName,CardCount,WipLimit");
        foreach (var wip in metrics.WipSnapshots)
        {
            sb.AppendLine($"{wip.ColumnId},{SanitizeCsvField(wip.ColumnName)},{wip.CardCount},{wip.WipLimit?.ToString(CultureInfo.InvariantCulture) ?? ""}");
        }
        sb.AppendLine();

        // Section: Blocked
        sb.AppendLine("[Blocked]");
        sb.AppendLine("CardId,CardTitle,BlockReason,BlockedDurationDays");
        foreach (var blocked in metrics.BlockedCards)
        {
            sb.AppendLine($"{blocked.CardId},{SanitizeCsvField(blocked.CardTitle)},{SanitizeCsvField(blocked.BlockReason ?? "")},{blocked.BlockedDurationDays.ToString(CultureInfo.InvariantCulture)}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Sanitize a field for safe CSV inclusion.
    /// - Strips CSV injection characters (=, +, -, @, tab, carriage return) from the start.
    /// - Quotes the field if it contains commas, quotes, or newlines.
    /// - Doubles internal quote characters.
    /// </summary>
    internal static string SanitizeCsvField(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        // Strip leading characters that could trigger formula injection in spreadsheet apps
        var sanitized = value;
        while (sanitized.Length > 0 && IsDangerousLeadingChar(sanitized[0]))
        {
            sanitized = sanitized[1..];
        }

        // If the field contains special CSV characters, quote it
        var needsQuoting = sanitized.Contains(',') ||
                           sanitized.Contains('"') ||
                           sanitized.Contains('\n') ||
                           sanitized.Contains('\r');

        if (needsQuoting)
        {
            sanitized = "\"" + sanitized.Replace("\"", "\"\"") + "\"";
        }

        return sanitized;
    }

    private static bool IsDangerousLeadingChar(char c)
        => c is '=' or '+' or '-' or '@' or '\t' or '\r';
}
