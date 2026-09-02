using System.Security.Cryptography;
using System.Text;

namespace Taskdeck.AccelerationCandidates;

public sealed record AutomationSafetyInput(Guid CandidateId, Guid OwnerUserId, CandidateRiskClass Risk, bool PermissionValid, bool PreviewApplyParity, bool CompensationProven, bool KillSwitchHealthy, double? CalibratedConfidence);
public sealed record AutomationSafetyDecision(bool Shadow, bool Canary, string Cohort, IReadOnlyList<string> Reasons);

public static class AutomationSafetyGate
{
    public static AutomationSafetyDecision Evaluate(AutomationSafetyInput input, ConfidencePolicy confidencePolicy, int canaryBasisPoints, string salt)
    {
        var reasons = new List<string>();
        var confidence = ConfidenceDecisionGate.Evaluate(input.CalibratedConfidence, input.Risk, confidencePolicy, input.PermissionValid, evidenceComplete: true);
        reasons.AddRange(confidence.Reasons);
        if (!input.PreviewApplyParity) reasons.Add("preview-apply-parity-unproven");
        if (!input.CompensationProven) reasons.Add("compensation-unproven");
        if (!input.KillSwitchHealthy) reasons.Add("kill-switch-unhealthy");
        var shadow = confidence.ShadowEligible;
        var operational = reasons.Count == 0 && confidence.CanaryEligible;
        var material = Encoding.UTF8.GetBytes($"{salt}|{input.OwnerUserId:N}|{input.CandidateId:N}");
        var bucket = BitConverter.ToUInt32(SHA256.HashData(material), 0) % 10_000;
        var canary = operational && bucket < Math.Clamp(canaryBasisPoints, 0, 10_000);
        return new AutomationSafetyDecision(shadow, canary, canary ? "canary" : shadow ? "shadow" : "excluded", reasons);
    }
}
