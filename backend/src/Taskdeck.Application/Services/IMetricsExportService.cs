using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

/// <summary>
/// Exports board metrics data as CSV with schema-versioned columns.
/// </summary>
public interface IMetricsExportService
{
    /// <summary>
    /// Generate a CSV export of board metrics for the given query.
    /// The acting user must have read access to the board.
    /// </summary>
    Task<Result<MetricsExportResult>> ExportCsvAsync(
        BoardMetricsQuery query,
        Guid actingUserId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a metrics export operation.
/// </summary>
/// <param name="Content">The raw CSV content bytes.</param>
/// <param name="FileName">Suggested file name for the download.</param>
/// <param name="ContentType">MIME type of the export.</param>
public sealed record MetricsExportResult(
    byte[] Content,
    string FileName,
    string ContentType);
