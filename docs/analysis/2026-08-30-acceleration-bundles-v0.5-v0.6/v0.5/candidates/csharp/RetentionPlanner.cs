namespace Taskdeck.AccelerationCandidates;

public sealed record RetentionPolicy(TimeSpan? RawSourceAge, TimeSpan? DerivedRepresentationAge, bool KeepAcceptedEvidence, bool DryRunOnly);
public sealed record RetentionSubject(Guid Id, string Kind, DateTimeOffset CreatedAt, bool Pinned, bool ReferencedByAcceptedEvidence, int ActiveReferenceCount);
public sealed record RetentionDecision(Guid Id, bool Delete, string Reason);

public static class RetentionPlanner
{
    public static IReadOnlyList<RetentionDecision> Plan(IEnumerable<RetentionSubject> subjects, RetentionPolicy policy, DateTimeOffset now)
    {
        var result = new List<RetentionDecision>();
        foreach (var subject in subjects.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id))
        {
            if (subject.Pinned) { result.Add(new(subject.Id, false, "pinned")); continue; }
            if (policy.KeepAcceptedEvidence && subject.ReferencedByAcceptedEvidence) { result.Add(new(subject.Id, false, "accepted-evidence")); continue; }
            if (subject.ActiveReferenceCount > 0) { result.Add(new(subject.Id, false, "active-references")); continue; }
            var age = subject.Kind == "source" ? policy.RawSourceAge : policy.DerivedRepresentationAge;
            if (age is null || now - subject.CreatedAt < age) { result.Add(new(subject.Id, false, "not-expired")); continue; }
            result.Add(new(subject.Id, !policy.DryRunOnly, policy.DryRunOnly ? "would-delete" : "expired"));
        }
        return result;
    }
}
