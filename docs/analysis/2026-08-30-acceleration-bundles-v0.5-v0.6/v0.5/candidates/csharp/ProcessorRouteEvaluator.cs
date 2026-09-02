namespace Taskdeck.AccelerationCandidates;

public sealed record ProcessorOffer(string Id, string Version, bool Healthy, EgressClass Egress, decimal EstimatedCost, IReadOnlySet<string> Capabilities);
public sealed record RejectedProcessor(string Id, string Reason);
public sealed record ProcessorRouteDecision(string? SelectedId, string? SelectedVersion, IReadOnlyList<RejectedProcessor> Rejected, string PolicyDigest);

public static class ProcessorRouteEvaluator
{
    public static ProcessorRouteDecision Select(string capability, ProcessingPolicySnapshot policy, IEnumerable<ProcessorOffer> offers, DateTimeOffset now)
    {
        policy.Validate(now);
        var rejected = new List<RejectedProcessor>();
        var eligible = new List<ProcessorOffer>();
        foreach (var offer in offers.OrderBy(x => x.EstimatedCost).ThenBy(x => x.Id, StringComparer.Ordinal))
        {
            string? reason = null;
            if (!policy.AllowedProcessorIds.Contains(offer.Id, StringComparer.Ordinal)) reason = "not-allowed";
            else if (!offer.Capabilities.Contains(capability)) reason = "capability-missing";
            else if (!offer.Healthy) reason = "unhealthy";
            else if (offer.EstimatedCost > policy.MaxEstimatedCost) reason = "cost-ceiling";
            else if (policy.Egress == EgressClass.LocalOnly && offer.Egress != EgressClass.LocalOnly) reason = "egress-forbidden";
            if (reason is null) eligible.Add(offer); else rejected.Add(new RejectedProcessor(offer.Id, reason));
        }
        var selected = eligible.FirstOrDefault();
        return new ProcessorRouteDecision(selected?.Id, selected?.Version, rejected, policy.ComputeDigest());
    }
}
