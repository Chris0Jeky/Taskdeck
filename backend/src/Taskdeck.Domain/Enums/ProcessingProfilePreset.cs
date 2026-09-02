namespace Taskdeck.Domain.Enums;

/// <summary>
/// The processing-profile presets (ADR-0065 §"Three independent policy families"; CF-10 <c>#2264</c>).
/// A processing profile decides egress class, approved providers and regions, local device use,
/// quality-versus-latency, budgets, escalation and retention. It is one of three deliberately separate
/// vocabularies: it never names a presentation profile (Flow · Guided · Control) or an authority
/// profile (ADR-0057's Observe · Suggest · Assist · Operate · Autonomous · Custom). <c>Strict</c> was
/// <c>Controlled</c> until 2026-08-30 and was renamed so no processing preset can be confused with the
/// <c>Control</c> presentation. <b>Not implemented yet:</b> no profile record exists — this is vocabulary
/// scaffolded ahead of CF-10, which defines each preset's dimensions. The ordinals carry no ordering of
/// restrictiveness; only <see cref="Private"/> (the default and zero value) and <see cref="Balanced"/>
/// have semantics fixed by the ADR and the issue today.
/// </summary>
public enum ProcessingProfilePreset
{
    /// <summary>Local-only processing; a remote processor is never eligible, even when it is the only one that could do the job (CF-10 acceptance).</summary>
    Private = 0,

    /// <summary>The fresh-install default (ruling 5): local and deterministic first, remote only after a one-time consent naming the destination and data class.</summary>
    Balanced = 1,

    /// <summary>A tighter-than-Balanced preset; its exact dimensions (approved destinations, budgets, escalation) are CF-10's to define.</summary>
    Strict = 2,

    /// <summary>Every dimension set explicitly by the user; no preset defaults apply.</summary>
    Expert = 3
}
