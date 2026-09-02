namespace Taskdeck.Domain.Enums;

/// <summary>
/// Where a processing profile allows a processor to run (ADR-0065 §Decision 6; CF-10 <c>#2264</c>).
/// CF-10's router v1 will evaluate this as a hard constraint before ordered preference: a processor whose
/// locality is outside the profile's egress class is ineligible and appears in the route receipt with a
/// stable rejection code, never silently skipped or silently overridden. <b>Not implemented yet:</b> no
/// profile, router or receipt exists — this is vocabulary scaffolded ahead of CF-10. Values are ordered
/// from most to least restrictive, and the zero value is the most restrictive.
/// </summary>
public enum ProcessingEgressClass
{
    /// <summary>In-process and local sidecar processors only; nothing leaves the machine (the default).</summary>
    LocalOnly = 0,

    /// <summary>Local processors plus remote destinations the owner has explicitly approved (and consented to, per destination and data class).</summary>
    ApprovedDestinations = 1,

    /// <summary>Any processor the operator has configured; consent per destination still applies.</summary>
    AnyConfigured = 2
}
