namespace Taskdeck.Domain.Enums;

/// <summary>
/// What the user asked Taskdeck to do with a capture (ADR-0065 §Decision 2).
/// <list type="bullet">
/// <item><see cref="Remember"/> — preserve it; never extract work automatically (the shipped
/// <see cref="CaptureDisposition.Kept"/> path).</item>
/// <item><see cref="Organize"/> — derive candidates and suggest context; plan nothing yet.</item>
/// <item><see cref="Act"/> — plan changes under the authority profile (the shipped
/// <see cref="CaptureDisposition.ProposalRequested"/> path; review-first until a delegated-authority
/// slice is separately gated).</item>
/// <item><see cref="Auto"/> — infer the mode conservatively; the inference is recorded, never silent.</item>
/// </list>
/// </summary>
public enum CaptureIntentMode
{
    Remember = 0,
    Organize = 1,
    Act = 2,
    Auto = 3
}
