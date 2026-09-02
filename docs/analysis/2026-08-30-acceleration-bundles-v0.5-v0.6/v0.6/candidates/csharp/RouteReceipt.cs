
using System.Collections.Immutable;

namespace Taskdeck.Acceleration.V06;

public enum ProcessorEligibility
{
    Eligible = 0,
    Ineligible = 1,
    EligibleNotChosen = 2,
    Chosen = 3
}

public sealed record ProcessorRouteAlternative(
    string ProcessorId,
    string ProcessorVersion,
    string? Model,
    string ExecutionMode,
    ProcessorEligibility Eligibility,
    ImmutableArray<string> ReasonCodes,
    decimal? EstimatedCost,
    string? Currency);

public sealed record ProcessingRouteReceipt(
    Guid Id,
    Guid JobId,
    Guid CaptureId,
    Guid OwnerUserId,
    string Capability,
    Guid PolicySnapshotId,
    string PolicyDigest,
    string RegistryDigest,
    DateTimeOffset EvaluatedAt,
    string? ChosenProcessorId,
    Guid? ConsentGrantId,
    Guid? BudgetReservationId,
    bool CacheHit,
    bool ForcedRerun,
    string? ForcedRerunReason,
    ImmutableArray<ProcessorRouteAlternative> Alternatives)
{
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Alternatives.IsDefaultOrEmpty) errors.Add("route.alternatives.empty");

        var chosen = Alternatives.Where(x => x.Eligibility == ProcessorEligibility.Chosen).ToList();
        if (ChosenProcessorId is null && chosen.Count > 0) errors.Add("route.chosen.unexpected");
        if (ChosenProcessorId is not null &&
            (chosen.Count != 1 || !StringComparer.Ordinal.Equals(chosen[0].ProcessorId, ChosenProcessorId)))
            errors.Add("route.chosen.mismatch");

        if (Alternatives.Select(x => x.ProcessorId).Distinct(StringComparer.Ordinal).Count() != Alternatives.Length)
            errors.Add("route.alternative.duplicate");

        if (ForcedRerun && string.IsNullOrWhiteSpace(ForcedRerunReason))
            errors.Add("route.forced-rerun.reason-required");

        return errors;
    }
}
