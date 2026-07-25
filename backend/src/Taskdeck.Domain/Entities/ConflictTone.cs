namespace Taskdeck.Domain.Entities;

/// <summary>
/// Tone classification for proposal conflict/warning rows.
/// Maps to frontend color semantics: Warn = rust, Info = mute, Ok = sage.
/// </summary>
public enum ConflictTone
{
    Warn = 0,
    Info = 1,
    Ok = 2
}
