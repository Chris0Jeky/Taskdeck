using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

/// <summary>
/// Computes daily cadence snapshots by querying audit log entries for a user
/// on a given day, bucketing them into 24 hourly slots, and deriving
/// first/peak/last action metadata.
/// </summary>
public class CadenceService : ICadenceService
{
    private readonly IAuditLogRepository _auditLogRepository;

    /// <summary>
    /// Upper bound on audit entries fetched per day to prevent unbounded queries.
    /// A single user generating more than 10,000 auditable actions in one day is
    /// well beyond any reasonable usage pattern.
    /// </summary>
    private const int MaxDailyEntries = 10_000;

    public CadenceService(IAuditLogRepository auditLogRepository)
    {
        _auditLogRepository = auditLogRepository ?? throw new ArgumentNullException(nameof(auditLogRepository));
    }

    public async Task<Result<CadenceSnapshot>> GetDailyCadenceAsync(
        Guid userId,
        DateTimeOffset date,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return Result.Failure<CadenceSnapshot>(
                ErrorCodes.ValidationError,
                "User ID is required.");
        }

        // Normalize to the start and end of the given UTC day.
        var dayStart = new DateTimeOffset(date.UtcDateTime.Date, TimeSpan.Zero);
        var dayEnd = dayStart.AddDays(1).AddTicks(-1);

        var entries = await _auditLogRepository.QueryAsync(
            from: dayStart,
            to: dayEnd,
            userId: userId,
            limit: MaxDailyEntries,
            cancellationToken: cancellationToken);

        var entryList = entries.ToList();

        if (entryList.Count == 0)
        {
            return Result.Success(CadenceSnapshot.Empty());
        }

        // Build per-hour counts.
        var hourCounts = new int[24];
        DateTimeOffset? firstAction = null;
        DateTimeOffset? lastAction = null;

        foreach (var entry in entryList)
        {
            var hour = entry.Timestamp.UtcDateTime.Hour;
            hourCounts[hour]++;

            if (firstAction is null || entry.Timestamp < firstAction)
                firstAction = entry.Timestamp;

            if (lastAction is null || entry.Timestamp > lastAction)
                lastAction = entry.Timestamp;
        }

        // Find peak hour (highest event count; ties go to earliest hour).
        int? peakHour = null;
        var peakCount = 0;
        for (var h = 0; h < 24; h++)
        {
            if (hourCounts[h] > peakCount)
            {
                peakCount = hourCounts[h];
                peakHour = h;
            }
        }

        var buckets = Enumerable.Range(0, 24)
            .Select(h => new CadenceBucket(h, hourCounts[h]))
            .ToList()
            .AsReadOnly();

        var snapshot = new CadenceSnapshot(buckets, firstAction, peakHour, lastAction);
        return Result.Success(snapshot);
    }
}
