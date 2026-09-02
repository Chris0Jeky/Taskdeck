
namespace Taskdeck.Acceleration.V06;

public sealed record SpeakerAlias(
    Guid BoardId,
    string SpeakerLabel,
    Guid ParticipantUserId,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt);

public sealed record SpeakerResolution(
    string SpeakerLabel,
    Guid? ParticipantUserId,
    string ResolutionCode);

public static class SpeakerAliasResolver
{
    public static SpeakerResolution Resolve(
        Guid boardId,
        string label,
        IReadOnlyCollection<SpeakerAlias> aliases,
        IReadOnlySet<Guid> authorizedParticipants)
    {
        var matches = aliases
            .Where(x => x.BoardId == boardId && StringComparer.Ordinal.Equals(x.SpeakerLabel, label))
            .ToList();

        if (matches.Count == 0)
            return new SpeakerResolution(label, null, "speaker.unresolved");
        if (matches.Select(x => x.ParticipantUserId).Distinct().Count() != 1)
            return new SpeakerResolution(label, null, "speaker.alias-conflict");

        var participant = matches[0].ParticipantUserId;
        return authorizedParticipants.Contains(participant)
            ? new SpeakerResolution(label, participant, "speaker.explicit-alias")
            : new SpeakerResolution(label, null, "speaker.participant-not-authorized");
    }
}

public enum MeetingRegisterKind
{
    Action = 0,
    Decision = 1,
    Question = 2,
    Risk = 3
}

public sealed record MeetingRegisterEntry(
    Guid CandidateId,
    Guid CandidateRevisionId,
    MeetingRegisterKind Kind,
    Guid CaptureId,
    Guid? ParticipantUserId,
    string SpeakerResolutionCode,
    IReadOnlyList<Guid> EvidenceAnchorIds,
    string State);

public sealed record MeetingConflictFact(
    string Code,
    Guid CandidateId,
    Guid? RelatedWorkItemId,
    string Severity);
