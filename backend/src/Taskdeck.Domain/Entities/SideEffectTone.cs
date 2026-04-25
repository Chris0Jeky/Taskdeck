namespace Taskdeck.Domain.Entities;

/// <summary>
/// Classifies whether a side-effect category is actively mutated by a proposal
/// or passively unaffected.
/// </summary>
public enum SideEffectTone
{
    /// <summary>The proposal actively creates or modifies artifacts in this category.</summary>
    Active,

    /// <summary>The proposal does not create or modify artifacts in this category.</summary>
    Passive
}
