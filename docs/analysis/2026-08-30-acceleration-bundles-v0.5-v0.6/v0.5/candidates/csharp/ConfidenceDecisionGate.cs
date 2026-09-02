namespace Taskdeck.AccelerationCandidates;

public enum CandidateRiskClass { Informational, ReversibleLow, ReversibleMedium, IrreversibleOrExternal }
public sealed record ConfidencePolicy(double SuggestThreshold, double ShadowThreshold, double CanaryThreshold);
public sealed record ConfidenceDecision(bool Suggest, bool ShadowEligible, bool CanaryEligible, IReadOnlyList<string> Reasons);

public static class ConfidenceDecisionGate
{
    public static ConfidenceDecision Evaluate(double? calibratedConfidence, CandidateRiskClass risk, ConfidencePolicy policy, bool permissionValid, bool evidenceComplete)
    {
        var reasons = new List<string>();
        if (!permissionValid) reasons.Add("permission-invalid");
        if (!evidenceComplete) reasons.Add("evidence-incomplete");
        if (calibratedConfidence is null or < 0 or > 1 || double.IsNaN(calibratedConfidence.Value))
            reasons.Add("confidence-unavailable");

        var valid = reasons.Count == 0;
        var value = calibratedConfidence ?? 0;
        var suggest = valid && value >= policy.SuggestThreshold;
        var shadow = suggest && risk != CandidateRiskClass.IrreversibleOrExternal && value >= policy.ShadowThreshold;
        var canary = shadow && risk == CandidateRiskClass.ReversibleLow && value >= policy.CanaryThreshold;
        if (risk == CandidateRiskClass.IrreversibleOrExternal) reasons.Add("risk-excluded-from-automation");
        return new ConfidenceDecision(suggest, shadow, canary, reasons);
    }
}
