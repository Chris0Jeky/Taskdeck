using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class NotificationPreferenceStateMachineTests
{
    private static readonly Guid ValidUserId = Guid.NewGuid();

    private static NotificationPreference CreateDefault() =>
        NotificationPreference.CreateDefault(ValidUserId);

    #region Constructor validation

    [Fact]
    public void Constructor_ValidUserId_CreatesWithDefaults()
    {
        var pref = CreateDefault();

        pref.UserId.Should().Be(ValidUserId);
        pref.InAppChannelEnabled.Should().BeTrue();
        pref.MentionImmediateEnabled.Should().BeTrue();
        pref.MentionDigestEnabled.Should().BeFalse();
        pref.AssignmentImmediateEnabled.Should().BeTrue();
        pref.AssignmentDigestEnabled.Should().BeFalse();
        pref.ProposalOutcomeImmediateEnabled.Should().BeTrue();
        pref.ProposalOutcomeDigestEnabled.Should().BeFalse();
    }

    [Fact]
    public void Constructor_EmptyUserId_Throws()
    {
        var act = () => new NotificationPreference(Guid.Empty);
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_CustomValues_SetsAll()
    {
        var pref = new NotificationPreference(
            ValidUserId,
            inAppChannelEnabled: false,
            mentionImmediateEnabled: false,
            mentionDigestEnabled: true,
            assignmentImmediateEnabled: false,
            assignmentDigestEnabled: true,
            proposalOutcomeImmediateEnabled: false,
            proposalOutcomeDigestEnabled: true);

        pref.InAppChannelEnabled.Should().BeFalse();
        pref.MentionImmediateEnabled.Should().BeFalse();
        pref.MentionDigestEnabled.Should().BeTrue();
        pref.AssignmentImmediateEnabled.Should().BeFalse();
        pref.AssignmentDigestEnabled.Should().BeTrue();
        pref.ProposalOutcomeImmediateEnabled.Should().BeFalse();
        pref.ProposalOutcomeDigestEnabled.Should().BeTrue();
    }

    #endregion

    #region Update

    [Fact]
    public void Update_AllTrue_SetsAll()
    {
        var pref = CreateDefault();

        pref.Update(true, true, true, true, true, true, true);

        pref.InAppChannelEnabled.Should().BeTrue();
        pref.MentionImmediateEnabled.Should().BeTrue();
        pref.MentionDigestEnabled.Should().BeTrue();
        pref.AssignmentImmediateEnabled.Should().BeTrue();
        pref.AssignmentDigestEnabled.Should().BeTrue();
        pref.ProposalOutcomeImmediateEnabled.Should().BeTrue();
        pref.ProposalOutcomeDigestEnabled.Should().BeTrue();
    }

    [Fact]
    public void Update_AllFalse_SetsAll()
    {
        var pref = CreateDefault();

        pref.Update(false, false, false, false, false, false, false);

        pref.InAppChannelEnabled.Should().BeFalse();
        pref.MentionImmediateEnabled.Should().BeFalse();
        pref.MentionDigestEnabled.Should().BeFalse();
        pref.AssignmentImmediateEnabled.Should().BeFalse();
        pref.AssignmentDigestEnabled.Should().BeFalse();
        pref.ProposalOutcomeImmediateEnabled.Should().BeFalse();
        pref.ProposalOutcomeDigestEnabled.Should().BeFalse();
    }

    [Fact]
    public void Update_UpdatesTimestamp()
    {
        var pref = CreateDefault();
        var before = pref.UpdatedAt;

        pref.Update(false, false, false, false, false, false, false);

        pref.UpdatedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void Update_CanBeCalledMultipleTimes()
    {
        var pref = CreateDefault();

        pref.Update(false, false, false, false, false, false, false);
        pref.Update(true, true, true, true, true, true, true);

        pref.InAppChannelEnabled.Should().BeTrue();
        pref.MentionDigestEnabled.Should().BeTrue();
    }

    #endregion
}
