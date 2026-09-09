using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

public sealed record PersonalPlanEntry(Guid BoardId, Guid CardId, DateOnly PlannedDate);
public sealed record PersonalPlanFocus(Guid BoardId, Guid CardId, DateTimeOffset WorkedAt);
public sealed record PersonalPlan(IReadOnlyList<PersonalPlanEntry> Entries, PersonalPlanFocus? LastWorked)
{
    public static PersonalPlan Empty => new([], null);

    public void Validate()
    {
        if (Entries is null || Entries.Count > 40)
            throw new DomainException(ErrorCodes.ValidationError, "Choose at most 40 cards for your personal plan.");
        var ids = new HashSet<Guid>();
        foreach (var entry in Entries)
            if (entry is null || entry.BoardId == Guid.Empty || entry.CardId == Guid.Empty ||
                entry.PlannedDate == default || !ids.Add(entry.CardId))
                throw new DomainException(ErrorCodes.ValidationError, "Each planned card needs a unique card ID, a board and a valid date.");
        if (LastWorked is not null && (LastWorked.BoardId == Guid.Empty || LastWorked.CardId == Guid.Empty))
            throw new DomainException(ErrorCodes.ValidationError, "Focus needs a board and card.");
    }
}
