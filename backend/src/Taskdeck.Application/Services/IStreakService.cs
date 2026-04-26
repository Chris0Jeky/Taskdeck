using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Services;

/// <summary>
/// Provides streak data (daily activity + sealed status) for a user
/// over a configurable window, driving the Today Streak grid.
/// </summary>
public interface IStreakService
{
    /// <summary>
    /// Returns streak data for the specified user over the last <paramref name="dayCount"/> days.
    /// </summary>
    /// <param name="userId">The authenticated user's ID.</param>
    /// <param name="dayCount">Number of days to include (default 90).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<StreakResult>> GetStreakAsync(
        Guid userId,
        int dayCount = 90,
        CancellationToken cancellationToken = default);
}
