using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

public class NotificationPreference : Entity
{
    public Guid UserId { get; private set; }
    public bool InAppChannelEnabled { get; private set; }

    public bool MentionImmediateEnabled { get; private set; }
    public bool MentionDigestEnabled { get; private set; }

    public bool AssignmentImmediateEnabled { get; private set; }
    public bool AssignmentDigestEnabled { get; private set; }

    public bool ProposalOutcomeImmediateEnabled { get; private set; }
    public bool ProposalOutcomeDigestEnabled { get; private set; }

    public User User { get; private set; } = null!;

    private NotificationPreference() : base()
    {
    }

    public NotificationPreference(
        Guid userId,
        bool inAppChannelEnabled = true,
        bool mentionImmediateEnabled = true,
        bool mentionDigestEnabled = false,
        bool assignmentImmediateEnabled = true,
        bool assignmentDigestEnabled = false,
        bool proposalOutcomeImmediateEnabled = true,
        bool proposalOutcomeDigestEnabled = false)
        : base()
    {
        if (userId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "User ID cannot be empty");

        UserId = userId;
        InAppChannelEnabled = inAppChannelEnabled;
        MentionImmediateEnabled = mentionImmediateEnabled;
        MentionDigestEnabled = mentionDigestEnabled;
        AssignmentImmediateEnabled = assignmentImmediateEnabled;
        AssignmentDigestEnabled = assignmentDigestEnabled;
        ProposalOutcomeImmediateEnabled = proposalOutcomeImmediateEnabled;
        ProposalOutcomeDigestEnabled = proposalOutcomeDigestEnabled;
    }

    public static NotificationPreference CreateDefault(Guid userId)
    {
        return new NotificationPreference(userId);
    }

    public void Update(
        bool inAppChannelEnabled,
        bool mentionImmediateEnabled,
        bool mentionDigestEnabled,
        bool assignmentImmediateEnabled,
        bool assignmentDigestEnabled,
        bool proposalOutcomeImmediateEnabled,
        bool proposalOutcomeDigestEnabled)
    {
        InAppChannelEnabled = inAppChannelEnabled;
        MentionImmediateEnabled = mentionImmediateEnabled;
        MentionDigestEnabled = mentionDigestEnabled;
        AssignmentImmediateEnabled = assignmentImmediateEnabled;
        AssignmentDigestEnabled = assignmentDigestEnabled;
        ProposalOutcomeImmediateEnabled = proposalOutcomeImmediateEnabled;
        ProposalOutcomeDigestEnabled = proposalOutcomeDigestEnabled;
        Touch();
    }
}
