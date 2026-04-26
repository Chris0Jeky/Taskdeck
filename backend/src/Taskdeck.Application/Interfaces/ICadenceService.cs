using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Interfaces;

/// <summary>
/// Provides daily cadence aggregation: per-hour activity buckets
/// derived from audit log entries for a given user and day.
/// </summary>
public interface ICadenceService
{
    /// <summary>
    /// Compute the per-hour cadence snapshot for the given user on the specified date.
    /// Returns 24 buckets (hours 0-23) with event counts, plus first/peak/last timestamps.
    /// </summary>
    /// <param name="userId">The user whose activity to aggregate.</param>
    /// <param name="date">The date to aggregate (only the date portion is used).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A cadence snapshot for the day, or an empty snapshot if no activity.</returns>
    Task<Result<CadenceSnapshot>> GetDailyCadenceAsync(
        Guid userId,
        DateTimeOffset date,
        CancellationToken cancellationToken = default);
}
