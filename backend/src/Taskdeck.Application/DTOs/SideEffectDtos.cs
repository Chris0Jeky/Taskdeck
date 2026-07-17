namespace Taskdeck.Application.DTOs;

public record SideEffectRowDto(
    string Key,
    string Value,
    string Tone // "active" | "passive"
);

/// <summary>
/// Compatibility shape for the side-effect endpoint's historical <c>reversibility</c> field.
/// Summary and Description describe apply risk and possible manual recovery. WindowMs is retained
/// as legacy review-attention metadata; it does not promise an undo capability.
/// </summary>
public record ReversibilityDto(
    string Summary,
    string Description,
    long WindowMs
);

public record ProposalSideEffectsDto(
    IReadOnlyList<SideEffectRowDto> Rows,
    // Property name retained to keep the existing JSON contract stable.
    ReversibilityDto Reversibility
);
