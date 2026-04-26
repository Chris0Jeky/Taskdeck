namespace Taskdeck.Application.DTOs;

public record SideEffectRowDto(
    string Key,
    string Value,
    string Tone // "active" | "passive"
);

public record ReversibilityDto(
    string Summary,
    string Description,
    long WindowMs
);

public record ProposalSideEffectsDto(
    IReadOnlyList<SideEffectRowDto> Rows,
    ReversibilityDto Reversibility
);
