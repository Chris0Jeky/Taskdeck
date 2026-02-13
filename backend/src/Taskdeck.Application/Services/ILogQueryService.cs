using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

public interface ILogQueryService
{
    Task<Result<IEnumerable<LogEntryDto>>> QueryLogsAsync(LogQueryDto query, CancellationToken ct = default);
    Task<Result<IEnumerable<LogEntryDto>>> GetByCorrelationIdAsync(string correlationId, CancellationToken ct = default);
    IAsyncEnumerable<LogStreamEvent> StreamLogsAsync(LogQueryDto? filter = null, CancellationToken ct = default);
}
