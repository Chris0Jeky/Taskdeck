using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

public class DailySnapshot : Entity
{
    public Guid UserId { get; private set; }
    public DateOnly Date { get; private set; }
    public DateTimeOffset? SealedAt { get; private set; }

    public bool IsSealed => SealedAt.HasValue;

    private DailySnapshot() { } // EF Core

    public DailySnapshot(Guid userId, DateOnly date, DateTimeOffset now)
    {
        if (userId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "UserId cannot be empty");

        if (date > DateOnly.FromDateTime(now.UtcDateTime))
            throw new DomainException(ErrorCodes.ValidationError, "Date must not be in the future");

        UserId = userId;
        Date = date;
    }

    /// <summary>
    /// Seals the day's snapshot. Idempotent: if already sealed, this is a no-op.
    /// </summary>
    public void Seal(DateTimeOffset now)
    {
        if (IsSealed)
            return;

        SealedAt = now;
        Touch();
    }
}
