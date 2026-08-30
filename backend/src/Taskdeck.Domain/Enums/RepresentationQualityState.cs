namespace Taskdeck.Domain.Enums;

/// <summary>
/// How much a derived representation can be trusted (ADR-0065 §Decision 3; part of the
/// representation header CF-06 backfills). Quality is a state of the representation, not of the
/// capture: a provisional streaming transcript and its final replacement are two headers with a
/// supersession link, never one row rewritten in place.
/// </summary>
public enum RepresentationQualityState
{
    /// <summary>Emitted before the processor finished (a streaming partial); expected to be superseded.</summary>
    Provisional = 0,

    /// <summary>The processor's complete output for this run.</summary>
    Final = 1,

    /// <summary>A person confirmed or corrected it; evidence anchored to it is user-verified.</summary>
    Verified = 2,

    /// <summary>Replaced by a later representation (a rerun, an escalation, a correction); kept for lineage.</summary>
    Superseded = 3
}
