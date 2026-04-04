using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class UserPreferenceStateMachineTests
{
    private static readonly Guid ValidUserId = Guid.NewGuid();

    private static UserPreference CreateDefault() =>
        UserPreference.CreateDefault(ValidUserId);

    #region Constructor validation

    [Fact]
    public void Constructor_ValidArgs_CreatesWithDefaults()
    {
        var pref = CreateDefault();

        pref.UserId.Should().Be(ValidUserId);
        pref.WorkspaceMode.Should().Be(WorkspaceMode.Guided);
        pref.OnboardingVisibility.Should().Be(WorkspaceOnboardingVisibility.Active);
        pref.OnboardingDismissedAt.Should().BeNull();
        pref.OnboardingCompletedAt.Should().BeNull();
    }

    [Fact]
    public void Constructor_EmptyUserId_Throws()
    {
        var act = () => new UserPreference(Guid.Empty);
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_CustomWorkspaceMode_Sets()
    {
        var pref = new UserPreference(ValidUserId, WorkspaceMode.Workbench);
        pref.WorkspaceMode.Should().Be(WorkspaceMode.Workbench);
    }

    [Fact]
    public void Constructor_InvalidWorkspaceMode_Throws()
    {
        var act = () => new UserPreference(ValidUserId, (WorkspaceMode)99);
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_InvalidOnboardingVisibility_Throws()
    {
        var act = () => new UserPreference(ValidUserId, onboardingVisibility: (WorkspaceOnboardingVisibility)99);
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    #endregion

    #region UpdateWorkspaceMode

    [Theory]
    [InlineData(WorkspaceMode.Guided)]
    [InlineData(WorkspaceMode.Workbench)]
    [InlineData(WorkspaceMode.Agent)]
    public void UpdateWorkspaceMode_ValidMode_Updates(WorkspaceMode mode)
    {
        var pref = CreateDefault();

        pref.UpdateWorkspaceMode(mode);

        pref.WorkspaceMode.Should().Be(mode);
    }

    [Fact]
    public void UpdateWorkspaceMode_InvalidMode_Throws()
    {
        var pref = CreateDefault();

        var act = () => pref.UpdateWorkspaceMode((WorkspaceMode)99);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void UpdateWorkspaceMode_UpdatesTimestamp()
    {
        var pref = CreateDefault();
        var before = pref.UpdatedAt;

        pref.UpdateWorkspaceMode(WorkspaceMode.Agent);

        pref.UpdatedAt.Should().BeOnOrAfter(before);
    }

    #endregion

    #region DismissOnboarding / ReplayOnboarding

    [Fact]
    public void DismissOnboarding_SetsVisibilityAndTimestamp()
    {
        var pref = CreateDefault();

        pref.DismissOnboarding();

        pref.OnboardingVisibility.Should().Be(WorkspaceOnboardingVisibility.Dismissed);
        pref.OnboardingDismissedAt.Should().NotBeNull();
        pref.OnboardingDismissedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void DismissOnboarding_IsIdempotent()
    {
        var pref = CreateDefault();

        pref.DismissOnboarding();
        var firstDismiss = pref.OnboardingDismissedAt;

        pref.DismissOnboarding();

        pref.OnboardingVisibility.Should().Be(WorkspaceOnboardingVisibility.Dismissed);
        // Timestamp updates each time (no guard), so it should be >= the first call
        pref.OnboardingDismissedAt.Should().BeOnOrAfter(firstDismiss!.Value);
    }

    [Fact]
    public void ReplayOnboarding_ResetsVisibilityAndClearsDismissedAt()
    {
        var pref = CreateDefault();
        pref.DismissOnboarding();

        pref.ReplayOnboarding();

        pref.OnboardingVisibility.Should().Be(WorkspaceOnboardingVisibility.Active);
        pref.OnboardingDismissedAt.Should().BeNull();
    }

    [Fact]
    public void ReplayOnboarding_IsIdempotent()
    {
        var pref = CreateDefault();

        pref.ReplayOnboarding();
        pref.ReplayOnboarding();

        pref.OnboardingVisibility.Should().Be(WorkspaceOnboardingVisibility.Active);
    }

    [Fact]
    public void DismissOnboarding_UpdatesTimestamp()
    {
        var pref = CreateDefault();
        var before = pref.UpdatedAt;

        pref.DismissOnboarding();

        pref.UpdatedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void ReplayOnboarding_UpdatesTimestamp()
    {
        var pref = CreateDefault();
        pref.DismissOnboarding();
        var before = pref.UpdatedAt;

        pref.ReplayOnboarding();

        pref.UpdatedAt.Should().BeOnOrAfter(before);
    }

    #endregion

    #region RecordOnboardingCompletion

    [Fact]
    public void RecordOnboardingCompletion_SetsCompletedAt()
    {
        var pref = CreateDefault();

        pref.RecordOnboardingCompletion();

        pref.OnboardingCompletedAt.Should().NotBeNull();
        pref.OnboardingCompletedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void RecordOnboardingCompletion_SecondCallIsNoOp()
    {
        var pref = CreateDefault();
        pref.RecordOnboardingCompletion();
        var firstCompletion = pref.OnboardingCompletedAt;
        var beforeSecondCall = pref.UpdatedAt;

        pref.RecordOnboardingCompletion();

        // Value should be unchanged — guard prevents overwrite
        pref.OnboardingCompletedAt.Should().Be(firstCompletion);
    }

    [Fact]
    public void RecordOnboardingCompletion_UpdatesTimestamp()
    {
        var pref = CreateDefault();
        var before = pref.UpdatedAt;

        pref.RecordOnboardingCompletion();

        pref.UpdatedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void RecordOnboardingCompletion_SecondCall_DoesNotUpdateTimestamp()
    {
        var pref = CreateDefault();
        pref.RecordOnboardingCompletion();
        var afterFirst = pref.UpdatedAt;

        pref.RecordOnboardingCompletion();

        // The guard returns early before Touch(), so UpdatedAt should not change
        pref.UpdatedAt.Should().Be(afterFirst);
    }

    #endregion

    #region Round-trip flows

    [Fact]
    public void Dismiss_Replay_Dismiss_Works()
    {
        var pref = CreateDefault();

        pref.DismissOnboarding();
        pref.ReplayOnboarding();
        pref.DismissOnboarding();

        pref.OnboardingVisibility.Should().Be(WorkspaceOnboardingVisibility.Dismissed);
    }

    [Fact]
    public void RecordCompletion_Then_Dismiss_Then_Replay_CompletionStillRecorded()
    {
        var pref = CreateDefault();
        pref.RecordOnboardingCompletion();
        var completedAt = pref.OnboardingCompletedAt;

        pref.DismissOnboarding();
        pref.ReplayOnboarding();

        pref.OnboardingCompletedAt.Should().Be(completedAt);
    }

    #endregion
}
