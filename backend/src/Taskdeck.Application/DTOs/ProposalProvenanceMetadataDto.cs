namespace Taskdeck.Application.DTOs;

/// <summary>
/// Proposal-scoped producer metadata for the Paper deep-Review provenance footnote and drawer
/// (#1987). Every value is server-recorded at proposal creation; nothing here is ever accepted
/// from a client.
/// <para>
/// The contract fails closed. <see cref="Provider"/> is null whenever no producer was recorded —
/// legacy rows, and origins (Chat, Manual) whose provenance carries only an origin label rather
/// than a real producer. The review surface must then make no producer claim at all: on a trust
/// surface, saying nothing is correct and guessing is not (#1963). <see cref="Model"/> and
/// <see cref="PromptVersion"/> are likewise null rather than invented, and are reported only
/// alongside a recorded provider so an origin sentinel is never rendered as a model name.
/// </para>
/// </summary>
/// <param name="Provider">Recorded producer (e.g. "openai", "deterministic"), or null.</param>
/// <param name="Model">Recorded model identifier, or null when no provider was recorded.</param>
/// <param name="PromptVersion">Recorded prompt contract version, or null.</param>
public record ProposalProvenanceMetadataDto(
    string? Provider,
    string? Model,
    string? PromptVersion);
