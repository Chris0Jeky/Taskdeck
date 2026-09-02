
namespace Taskdeck.Acceleration.V06;

public enum AuthorityShadowResult
{
    Ineligible = 0,
    WouldAllow = 1,
    WouldDeny = 2
}

public sealed record AuthorityShadowInput(
    Guid ProposalId,
    Guid ProposalRevisionId,
    Guid OwnerUserId,
    Guid TargetBoardId,
    bool ExplicitActIntent,
    bool ExplicitTarget,
    bool CurrentWritePermission,
    bool ExactlyOneCreateCardOperation,
    bool ExtractiveEvidenceComplete,
    bool LowRisk,
    bool PolicyActive,
    bool KillSwitchOff,
    bool DailyCeilingAvailable,
    bool CompensationAvailable);

public sealed record AuthorityShadowDecision(
    AuthorityShadowResult Result,
    IReadOnlyList<string> ReasonCodes);

public static class AuthorityShadowEvaluator
{
    public static AuthorityShadowDecision Evaluate(AuthorityShadowInput input)
    {
        var reasons = new List<string>();
        if (!input.ExplicitActIntent) reasons.Add("authority.intent-not-explicit-act");
        if (!input.ExplicitTarget) reasons.Add("authority.target-not-explicit");
        if (!input.CurrentWritePermission) reasons.Add("authority.permission-denied");
        if (!input.ExactlyOneCreateCardOperation) reasons.Add("authority.operation-class-ineligible");
        if (!input.ExtractiveEvidenceComplete) reasons.Add("authority.evidence-incomplete");
        if (!input.LowRisk) reasons.Add("authority.risk-not-low");
        if (!input.PolicyActive) reasons.Add("authority.policy-inactive");
        if (!input.KillSwitchOff) reasons.Add("authority.kill-switch-active");
        if (!input.DailyCeilingAvailable) reasons.Add("authority.daily-ceiling-exhausted");
        if (!input.CompensationAvailable) reasons.Add("authority.compensation-unavailable");

        return reasons.Count == 0
            ? new AuthorityShadowDecision(AuthorityShadowResult.WouldAllow, Array.Empty<string>())
            : new AuthorityShadowDecision(AuthorityShadowResult.WouldDeny, reasons);
    }

    public static Exception ExecutionIsForbidden() =>
        new InvalidOperationException("authority.shadow-only");
}
