namespace Taskdeck.Domain.Enums;

/// <summary>
/// How one registry candidate fared in a route evaluation (ADR-0065 §Decision 6; CF-10 <c>#2264</c>).
/// CF-10's route receipt will list every candidate exactly once with one of these three mutually
/// exclusive values, so the receipt explains the road not taken as well as the road taken. Rejection
/// reasons are stable kebab-case codes on the receipt entry, not members of this enum, so the vocabulary
/// can grow without renumbering. <b>Not implemented yet:</b> no router or receipt exists — this is
/// vocabulary scaffolded ahead of CF-10. The zero value fails closed: an entry whose outcome was never
/// written reads as ineligible.
/// </summary>
public enum ProcessorEligibility
{
    /// <summary>Failed at least one hard constraint (egress, consent, capability, media, language, deadline, budget, device, health), or never evaluated (the default).</summary>
    Ineligible = 0,

    /// <summary>Passed every hard constraint but ranked behind the chosen processor in the profile's ordered preference.</summary>
    EligibleNotChosen = 1,

    /// <summary>Passed every hard constraint and was first in the profile's ordered preference.</summary>
    Chosen = 2
}
