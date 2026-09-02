
using System.Collections.Immutable;

namespace Taskdeck.Acceleration.V06;

public sealed record ProcessorCandidate(
    string Id,
    string Version,
    string ExecutionMode,
    ImmutableHashSet<string> Capabilities,
    ImmutableHashSet<string> MediaTypes,
    ImmutableHashSet<string> Languages,
    ImmutableHashSet<string> Features,
    string? Host,
    string? Region,
    string DataClass,
    bool Healthy,
    bool RequiresNetwork,
    bool RequiresGpu,
    decimal? EstimatedCost,
    string? Currency);

public sealed record RouteRequest(
    Guid OwnerUserId,
    string Capability,
    string MediaType,
    string? Language,
    ImmutableHashSet<string> RequiredFeatures,
    ProcessingPolicySnapshot Policy,
    IReadOnlyList<ProcessingConsentGrant> Consents,
    DateTimeOffset Now);

public sealed record RouteDecision(
    string? ChosenProcessorId,
    ImmutableArray<ProcessorRouteAlternative> Alternatives);

public static class RouterV1
{
    public static RouteDecision Evaluate(RouteRequest request, IEnumerable<ProcessorCandidate> candidates)
    {
        var preference = request.Policy.Preferences
            .SingleOrDefault(x => StringComparer.Ordinal.Equals(x.Capability, request.Capability))
            ?.OrderedProcessorIds ?? ImmutableArray<string>.Empty;

        var preferenceIndex = preference
            .Select((id, index) => (id, index))
            .ToDictionary(x => x.id, x => x.index, StringComparer.Ordinal);

        var evaluated = candidates
            .OrderBy(x => preferenceIndex.TryGetValue(x.Id, out var index) ? index : int.MaxValue)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .Select(candidate => EvaluateOne(request, candidate))
            .ToList();

        var chosen = evaluated.FirstOrDefault(x => x.Eligibility == ProcessorEligibility.Eligible);
        if (chosen is not null)
        {
            for (var i = 0; i < evaluated.Count; i++)
            {
                evaluated[i] = evaluated[i] with
                {
                    Eligibility = StringComparer.Ordinal.Equals(evaluated[i].ProcessorId, chosen.ProcessorId)
                        ? ProcessorEligibility.Chosen
                        : evaluated[i].Eligibility == ProcessorEligibility.Eligible
                            ? ProcessorEligibility.EligibleNotChosen
                            : evaluated[i].Eligibility
                };
            }
        }

        return new RouteDecision(chosen?.ProcessorId, evaluated.ToImmutableArray());
    }

    private static ProcessorRouteAlternative EvaluateOne(RouteRequest request, ProcessorCandidate candidate)
    {
        var reasons = new List<string>();
        if (!candidate.Capabilities.Contains(request.Capability)) reasons.Add("capability-unsupported");
        if (!candidate.MediaTypes.Contains(request.MediaType)) reasons.Add("media-type-unsupported");
        if (request.Language is not null &&
            candidate.Languages.Count > 0 &&
            !candidate.Languages.Contains(request.Language))
            reasons.Add("language-unsupported");
        if (!request.RequiredFeatures.IsSubsetOf(candidate.Features)) reasons.Add("required-feature-missing");
        if (!candidate.Healthy) reasons.Add("processor-unhealthy");
        if (candidate.RequiresGpu && !request.Policy.AllowGpu) reasons.Add("gpu-disallowed");
        if (request.Policy.ApprovedProcessorIds.Length > 0 &&
            !request.Policy.ApprovedProcessorIds.Contains(candidate.Id, StringComparer.Ordinal))
            reasons.Add("processor-not-approved");

        if (candidate.RequiresNetwork)
        {
            if (request.Policy.EgressClass == ProcessingEgressClass.LocalOnly)
                reasons.Add("remote-disallowed");
            if (candidate.Host is null ||
                !request.Policy.ApprovedHosts.Contains(candidate.Host, StringComparer.OrdinalIgnoreCase))
                reasons.Add("host-not-approved");
            if (candidate.Region is not null &&
                request.Policy.ApprovedRegions.Length > 0 &&
                !request.Policy.ApprovedRegions.Contains(candidate.Region, StringComparer.Ordinal))
                reasons.Add("region-not-approved");
            if (!HasConsent(request, candidate))
                reasons.Add("consent-required");
        }

        if (request.Policy.PerCaptureCostCeiling.HasValue &&
            candidate.EstimatedCost.HasValue &&
            candidate.EstimatedCost.Value > request.Policy.PerCaptureCostCeiling.Value)
            reasons.Add("cost-ceiling-exceeded");

        return new ProcessorRouteAlternative(
            candidate.Id,
            candidate.Version,
            Model: null,
            candidate.ExecutionMode,
            reasons.Count == 0 ? ProcessorEligibility.Eligible : ProcessorEligibility.Ineligible,
            reasons.Order(StringComparer.Ordinal).ToImmutableArray(),
            candidate.EstimatedCost,
            candidate.Currency);
    }

    private static bool HasConsent(RouteRequest request, ProcessorCandidate candidate) =>
        !candidate.RequiresNetwork ||
        (candidate.Host is not null &&
         request.Consents.Any(x => x.Covers(
             request.OwnerUserId, candidate.Host, candidate.DataClass, candidate.Id, request.Now)));
}
