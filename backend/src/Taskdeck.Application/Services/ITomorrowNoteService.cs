using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

public interface ITomorrowNoteService
{
    /// <summary>
    /// Gets the note for a given date. The note was written on the previous day
    /// and is displayed on this date's morning open.
    /// </summary>
    Task<Result<TomorrowNoteResponse?>> GetNoteAsync(Guid userId, DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upsert: creates or updates the tomorrow note for the given date.
    /// Autosave-friendly -- idempotent for the same user+date pair.
    /// </summary>
    Task<Result<TomorrowNoteResponse>> SaveNoteAsync(Guid userId, DateOnly date, string text, CancellationToken cancellationToken = default);
}
