using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class LogQueryService : ILogQueryService
{
    private readonly IUnitOfWork _unitOfWork;

    public LogQueryService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<IEnumerable<LogEntryDto>>> QueryLogsAsync(LogQueryDto query, CancellationToken ct = default)
    {
        try
        {
            var auditLogs = await _unitOfWork.AuditLogs.GetAllAsync(ct);
            var entries = auditLogs.AsEnumerable();

            if (query.UserId.HasValue)
                entries = entries.Where(l => l.UserId == query.UserId.Value);

            if (!string.IsNullOrWhiteSpace(query.Source))
                entries = entries.Where(l => l.EntityType.Equals(query.Source, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(query.Level))
                entries = entries.Where(l => MapActionToLevel(l.Action).Equals(query.Level, StringComparison.OrdinalIgnoreCase));

            if (query.From.HasValue)
                entries = entries.Where(l => l.Timestamp >= query.From.Value);

            if (query.To.HasValue)
                entries = entries.Where(l => l.Timestamp <= query.To.Value);

            var results = entries
                .OrderByDescending(l => l.Timestamp)
                .Take(query.Limit)
                .Select(MapAuditLogToEntry)
                .ToList();

            return Result.Success<IEnumerable<LogEntryDto>>(results);
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<LogEntryDto>>(ErrorCodes.UnexpectedError, ex.Message);
        }
    }

    public async Task<Result<IEnumerable<LogEntryDto>>> GetByCorrelationIdAsync(string correlationId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
            return Result.Failure<IEnumerable<LogEntryDto>>(ErrorCodes.ValidationError, "Correlation ID cannot be empty");

        try
        {
            var commandRun = await _unitOfWork.CommandRuns.GetByCorrelationIdAsync(correlationId, ct);
            var entries = new List<LogEntryDto>();

            if (commandRun != null)
            {
                var runWithLogs = await _unitOfWork.CommandRuns.GetByIdWithLogsAsync(commandRun.Id, ct);
                if (runWithLogs != null)
                {
                    entries.AddRange(runWithLogs.Logs.Select(log => new LogEntryDto(
                        log.Id,
                        new DateTimeOffset(log.Timestamp, TimeSpan.Zero),
                        log.Level,
                        log.Source,
                        "CommandRunLog",
                        log.Message,
                        correlationId,
                        runWithLogs.RequestedByUserId,
                        null,
                        log.Metadata
                    )));
                }
            }

            return Result.Success<IEnumerable<LogEntryDto>>(entries.OrderByDescending(e => e.Timestamp));
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<LogEntryDto>>(ErrorCodes.UnexpectedError, ex.Message);
        }
    }

    public async IAsyncEnumerable<LogStreamEvent> StreamLogsAsync(
        LogQueryDto? filter = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var lastCheck = DateTimeOffset.UtcNow;
        var heartbeatTimer = DateTimeOffset.UtcNow;

        while (!ct.IsCancellationRequested)
        {
            var query = filter ?? new LogQueryDto();
            query = query with { From = lastCheck };

            var result = await QueryLogsAsync(query, ct);
            if (result.IsSuccess)
            {
                foreach (var entry in result.Value)
                {
                    yield return new LogStreamEvent("log.entry", entry);
                }
            }

            lastCheck = DateTimeOffset.UtcNow;

            if ((DateTimeOffset.UtcNow - heartbeatTimer).TotalSeconds >= 15)
            {
                yield return new LogStreamEvent("heartbeat");
                heartbeatTimer = DateTimeOffset.UtcNow;
            }

            await Task.Delay(2000, ct);
        }
    }

    private static LogEntryDto MapAuditLogToEntry(AuditLog log)
    {
        return new LogEntryDto(
            log.Id,
            log.Timestamp,
            MapActionToLevel(log.Action),
            log.EntityType,
            log.Action.ToString(),
            log.Changes ?? $"{log.Action} on {log.EntityType} ({log.EntityId})",
            null,
            log.UserId,
            null,
            log.Changes
        );
    }

    private static string MapActionToLevel(AuditAction action)
    {
        return action switch
        {
            AuditAction.Created => "Info",
            AuditAction.Updated => "Info",
            AuditAction.Deleted => "Warning",
            AuditAction.Archived => "Warning",
            AuditAction.Unarchived => "Info",
            AuditAction.Moved => "Info",
            AuditAction.PermissionGranted => "Info",
            AuditAction.PermissionRevoked => "Warning",
            AuditAction.OwnershipTransferred => "Warning",
            _ => "Info"
        };
    }
}
