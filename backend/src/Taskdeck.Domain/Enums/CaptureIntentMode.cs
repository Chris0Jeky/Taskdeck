namespace Taskdeck.Domain.Enums;

/// <summary>
/// What the user asked Taskdeck to do with a capture (ADR-0065 §Decision 2, amended 2026-08-30).
/// A capture carries a <b>requested</b> intent (which may be <see cref="Auto"/>) and an
/// <b>effective</b> intent (never <see cref="Auto"/>): <see cref="Auto"/> is an instruction to
/// infer, not a result, and the inference is recorded against the run that made it
/// (<c>Capture.IntentResolvedByRunId</c>).
/// <list type="bullet">
/// <item><see cref="Remember"/> — preserve it; never extract work automatically (the shipped
/// <see cref="CaptureDisposition.Kept"/> path).</item>
/// <item><see cref="Organize"/> — derive candidates and suggest context; plan nothing yet.</item>
/// <item><see cref="Act"/> — plan changes under the authority profile (the shipped
/// <see cref="CaptureDisposition.ProposalRequested"/> path; review-first until a delegated-authority
/// slice is separately gated).</item>
/// <item><see cref="Auto"/> — infer the effective intent conservatively; valid only as a requested
/// intent.</item>
/// </list>
/// </summary>
public enum CaptureIntentMode
{
    Remember = 0,
    Organize = 1,
    Act = 2,
    Auto = 3
}
