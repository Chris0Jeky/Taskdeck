namespace Taskdeck.Domain.Enums;

/// <summary>
/// How one registry candidate fared in a route evaluation (ADR-0065 §Decision 6; CF-10 <c>#2264</c>).
/// A route receipt lists every candidate exactly once with one of these values, so the receipt explains
/// the road not taken as well as the road taken. Rejection reasons are stable kebab-case codes on the
/// receipt entry, not members of this enum, so the vocabulary can grow without renumbering.
/// </summary>
public enum ProcessorEligibility
{
    /// <summary>Passed every hard constraint and was first in the profile's ordered preference.</summary>
    Chosen = 0,

    /// <summary>Passed every hard constraint; recorded before the preference order was applied.</summary>
    Eligible = 1,

    /// <summary>Passed every hard constraint but ranked behind the chosen processor in preference order.</summary>
    EligibleNotChosen = 2,

    /// <summary>Failed at least one hard constraint (egress, consent, capability, media, language, deadline, budget, device, health).</summary>
    Ineligible = 3
}
