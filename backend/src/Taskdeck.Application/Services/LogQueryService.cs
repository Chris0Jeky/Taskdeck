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
            var validationResult = ValidateQuery(query);
            if (!validationResult.IsSuccess)
            {
                return Result.Failure<IEnumerable<LogEntryDto>>(validationResult.ErrorCode, validationResult.ErrorMessage);
            }

            var effectiveTo = query.To ?? DateTimeOffset.UtcNow;
            var effectiveFrom = query.From ?? effectiveTo.AddDays(-7);
            var limit = Math.Clamp(query.Limit, 1, 500);

            var auditEntries = await BuildAuditEntriesAsync(query, effectiveFrom, effectiveTo, ct);
            var commandEntries = await BuildCommandRunEntriesAsync(query, effectiveFrom, effectiveTo, ct);

            var combined = auditEntries
                .Concat(commandEntries)
                .Where(entry => MatchesLogLevelFilter(entry.Level, query.Level))
                .Where(entry => MatchesSourceFilter(entry.Source, query.Source))
                .Where(entry => MatchesCorrelationFilter(entry.CorrelationId, query.CorrelationId))
                .OrderByDescending(entry => entry.Timestamp)
                .Take(limit)
                .ToList();

            return Result.Success<IEnumerable<LogEntryDto>>(combined);
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

        var result = await QueryLogsAsync(new LogQueryDto(
            CorrelationId: correlationId,
            Limit: 500), ct);
        if (!result.IsSuccess)
        {
            return result;
        }
        if (!result.Value.Any())
        {
            return Result.Failure<IEnumerable<LogEntryDto>>(ErrorCodes.NotFound, $"No log entries found for correlation ID '{correlationId}'");
        }

        return result;
    }

    public async IAsyncEnumerable<LogStreamEvent> StreamLogsAsync(
        LogQueryDto? filter = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var streamStartedAt = DateTimeOffset.UtcNow;
        var lastCheck = DateTimeOffset.UtcNow.AddSeconds(-2);
        var heartbeatTimer = DateTimeOffset.UtcNow;

        while (!ct.IsCancellationRequested && DateTimeOffset.UtcNow - streamStartedAt < TimeSpan.FromMinutes(10))
        {
            var queryUpperBound = DateTimeOffset.UtcNow;
            var query = (filter ?? new LogQueryDto()) with
            {
                From = lastCheck,
                To = queryUpperBound
            };

            var result = await QueryLogsAsync(query, ct);
            if (result.IsSuccess)
            {
                foreach (var entry in result.Value.OrderBy(e => e.Timestamp))
                {
                    yield return new LogStreamEvent("log.entry", entry);
                }
            }

            // Advance cursor to the exact queried upper bound to avoid gaps.
            lastCheck = queryUpperBound;

            if ((DateTimeOffset.UtcNow - heartbeatTimer).TotalSeconds >= 15)
            {
                yield return new LogStreamEvent("heartbeat");
                heartbeatTimer = DateTimeOffset.UtcNow;
            }

            try
            {
                await Task.Delay(2000, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                yield break;
            }
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

    private async Task<List<LogEntryDto>> BuildAuditEntriesAsync(
        LogQueryDto query,
        DateTimeOffset effectiveFrom,
        DateTimeOffset effectiveTo,
        CancellationToken ct)
    {
        var auditLogs = await _unitOfWork.AuditLogs.GetAllAsync(ct);

        var entries = auditLogs
            .Where(log => log.Timestamp >= effectiveFrom && log.Timestamp <= effectiveTo)
            .Where(log => !query.UserId.HasValue || log.UserId == query.UserId.Value)
            .Where(log =>
                !query.BoardId.HasValue ||
                (log.EntityType.Equals("board", StringComparison.OrdinalIgnoreCase) && log.EntityId == query.BoardId.Value))
            .Select(MapAuditLogToEntry)
            .ToList();

        return entries;
    }

    private async Task<List<LogEntryDto>> BuildCommandRunEntriesAsync(
        LogQueryDto query,
        DateTimeOffset effectiveFrom,
        DateTimeOffset effectiveTo,
        CancellationToken ct)
    {
        var runs = await _unitOfWork.CommandRuns.GetAllAsync(ct);
        var filteredRuns = runs
            .Where(run => !query.UserId.HasValue || run.RequestedByUserId == query.UserId.Value)
            .Where(run => string.IsNullOrWhiteSpace(query.CorrelationId) || run.CorrelationId.Equals(query.CorrelationId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var entries = new List<LogEntryDto>();
        foreach (var run in filteredRuns)
        {
            var runWithLogs = await _unitOfWork.CommandRuns.GetByIdWithLogsAsync(run.Id, ct);
            if (runWithLogs == null)
            {
                continue;
            }

            entries.AddRange(runWithLogs.Logs
                .Where(log =>
                {
                    var timestamp = new DateTimeOffset(log.Timestamp, TimeSpan.Zero);
                    return timestamp >= effectiveFrom && timestamp <= effectiveTo;
                })
                .Select(log => new LogEntryDto(
                    log.Id,
                    new DateTimeOffset(log.Timestamp, TimeSpan.Zero),
                    log.Level,
                    log.Source,
                    "CommandRunLog",
                    log.Message,
                    run.CorrelationId,
                    run.RequestedByUserId,
                    null,
                    log.Metadata)));
        }

        return entries;
    }

    private static Result ValidateQuery(LogQueryDto query)
    {
        if (query.Limit <= 0 || query.Limit > 500)
        {
            return Result.Failure(ErrorCodes.ValidationError, "Limit must be between 1 and 500");
        }

        if (query.From.HasValue && query.To.HasValue && query.From > query.To)
        {
            return Result.Failure(ErrorCodes.ValidationError, "'from' must be less than or equal to 'to'");
        }

        var effectiveTo = query.To ?? DateTimeOffset.UtcNow;
        var effectiveFrom = query.From ?? effectiveTo.AddDays(-7);
        if (effectiveTo - effectiveFrom > TimeSpan.FromDays(30))
        {
            return Result.Failure(ErrorCodes.ValidationError, "Log query window cannot exceed 30 days");
        }

        return Result.Success();
    }

    private static bool MatchesLogLevelFilter(string entryLevel, string? requestedLevel)
    {
        return string.IsNullOrWhiteSpace(requestedLevel) ||
               entryLevel.Equals(requestedLevel, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesSourceFilter(string entrySource, string? requestedSource)
    {
        return string.IsNullOrWhiteSpace(requestedSource) ||
               entrySource.Equals(requestedSource, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesCorrelationFilter(string? entryCorrelationId, string? requestedCorrelationId)
    {
        return string.IsNullOrWhiteSpace(requestedCorrelationId) ||
               string.Equals(entryCorrelationId, requestedCorrelationId, StringComparison.OrdinalIgnoreCase);
    }
}
