using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

/// <summary>
/// Edge-case tests for the AutomationProposal state machine, covering
/// expiry timing boundaries, double-apply prevention, state machine violations,
/// and dismissal logic. Addresses issue #708 (TST-41).
/// </summary>
public class AutomationProposalLifecycleEdgeCaseTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _boardId = Guid.NewGuid();

    #region Expiry Timing Boundaries

    [Fact]
    public void Approve_ShouldThrow_WhenJustPastExpiry()
    {
        // Arrange: proposal whose expiry has just passed.
        // Use -1 second (not -1ms) to avoid clock-resolution flakiness on Windows (~15ms).
        var proposal = CreateProposal();
        SetExpiresAt(proposal, DateTime.UtcNow.AddSeconds(-1));

        // Act
        var act = () => proposal.Approve(Guid.NewGuid());

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Cannot approve expired proposal");
    }

    [Fact]
    public void IsExpired_ShouldBeTrue_WhenPastExpiry()
    {
        var proposal = CreateProposal();
        SetExpiresAt(proposal, DateTime.UtcNow.AddMinutes(-1));

        proposal.IsExpired.Should().BeTrue();
    }

    [Fact]
    public void IsExpired_ShouldBeFalse_WhenBeforeExpiry()
    {
        var proposal = CreateProposal();
        // Default expiry is 1440 minutes in the future
        proposal.IsExpired.Should().BeFalse();
    }

    [Fact]
    public void Expire_ShouldSucceed_EvenWhenExpiresAtIsInFuture()
    {
        // The Expire() method is a manual override that does not check ExpiresAt,
        // it only requires PendingReview status.
        var proposal = CreateProposal();

        proposal.Expire();

        proposal.Status.Should().Be(ProposalStatus.Expired);
    }

    [Fact]
    public void Approve_ShouldSucceed_WhenNotYetExpired()
    {
        var proposal = CreateProposal();
        // ExpiresAt defaults to 1440 minutes from now, well in the future

        proposal.Approve(Guid.NewGuid());

        proposal.Status.Should().Be(ProposalStatus.Approved);
    }

    #endregion

    #region Double-Apply Prevention

    [Fact]
    public void MarkAsApplied_ShouldThrow_WhenAlreadyApplied()
    {
        var proposal = CreateProposal();
        proposal.Approve(Guid.NewGuid());
        proposal.MarkAsApplied();

        var act = () => proposal.MarkAsApplied();

        act.Should().Throw<DomainException>()
            .WithMessage("Only approved proposals can be marked as applied");
    }

    [Fact]
    public void MarkAsFailed_ShouldThrow_WhenAlreadyApplied()
    {
        var proposal = CreateProposal();
        proposal.Approve(Guid.NewGuid());
        proposal.MarkAsApplied();

        var act = () => proposal.MarkAsFailed("Too late");

        act.Should().Throw<DomainException>()
            .WithMessage("Only approved proposals can be marked as failed");
    }

    [Fact]
    public void MarkAsFailed_ShouldThrow_WhenAlreadyFailed()
    {
        var proposal = CreateProposal();
        proposal.Approve(Guid.NewGuid());
        proposal.MarkAsFailed("First failure");

        var act = () => proposal.MarkAsFailed("Second failure");

        act.Should().Throw<DomainException>()
            .WithMessage("Only approved proposals can be marked as failed");
    }

    [Fact]
    public void MarkAsApplied_ShouldThrow_WhenAlreadyFailed()
    {
        var proposal = CreateProposal();
        proposal.Approve(Guid.NewGuid());
        proposal.MarkAsFailed("Something broke");

        var act = () => proposal.MarkAsApplied();

        act.Should().Throw<DomainException>()
            .WithMessage("Only approved proposals can be marked as applied");
    }

    #endregion

    #region State Machine Violations — Comprehensive Coverage

    [Fact]
    public void Approve_ShouldThrow_WhenExpired()
    {
        var proposal = CreateProposal();
        proposal.Expire();

        var act = () => proposal.Approve(Guid.NewGuid());

        act.Should().Throw<DomainException>()
            .WithMessage("Cannot approve proposal in status Expired");
    }

    [Fact]
    public void Reject_ShouldThrow_WhenExpired()
    {
        var proposal = CreateProposal();
        proposal.Expire();

        var act = () => proposal.Reject(Guid.NewGuid(), "Reason");

        act.Should().Throw<DomainException>()
            .WithMessage("Cannot reject proposal in status Expired");
    }

    [Fact]
    public void Expire_ShouldThrow_WhenApproved()
    {
        var proposal = CreateProposal();
        proposal.Approve(Guid.NewGuid());

        var act = () => proposal.Expire();

        act.Should().Throw<DomainException>()
            .WithMessage("Cannot expire proposal in status Approved");
    }

    [Fact]
    public void Expire_ShouldThrow_WhenRejected()
    {
        var proposal = CreateProposal();
        proposal.Reject(Guid.NewGuid());

        var act = () => proposal.Expire();

        act.Should().Throw<DomainException>()
            .WithMessage("Cannot expire proposal in status Rejected");
    }

    [Fact]
    public void Expire_ShouldThrow_WhenAlreadyExpired()
    {
        var proposal = CreateProposal();
        proposal.Expire();

        var act = () => proposal.Expire();

        act.Should().Throw<DomainException>()
            .WithMessage("Cannot expire proposal in status Expired");
    }

    [Fact]
    public void Expire_ShouldThrow_WhenApplied()
    {
        var proposal = CreateProposal();
        proposal.Approve(Guid.NewGuid());
        proposal.MarkAsApplied();

        var act = () => proposal.Expire();

        act.Should().Throw<DomainException>()
            .WithMessage("Cannot expire proposal in status Applied");
    }

    [Fact]
    public void Expire_ShouldThrow_WhenFailed()
    {
        var proposal = CreateProposal();
        proposal.Approve(Guid.NewGuid());
        proposal.MarkAsFailed("Failure");

        var act = () => proposal.Expire();

        act.Should().Throw<DomainException>()
            .WithMessage("Cannot expire proposal in status Failed");
    }

    [Fact]
    public void Approve_ShouldThrow_WhenApplied()
    {
        var proposal = CreateProposal();
        proposal.Approve(Guid.NewGuid());
        proposal.MarkAsApplied();

        var act = () => proposal.Approve(Guid.NewGuid());

        act.Should().Throw<DomainException>()
            .WithMessage("Cannot approve proposal in status Applied");
    }

    [Fact]
    public void Approve_ShouldThrow_WhenFailed()
    {
        var proposal = CreateProposal();
        proposal.Approve(Guid.NewGuid());
        proposal.MarkAsFailed("Failure");

        var act = () => proposal.Approve(Guid.NewGuid());

        act.Should().Throw<DomainException>()
            .WithMessage("Cannot approve proposal in status Failed");
    }

    [Fact]
    public void Reject_ShouldThrow_WhenApplied()
    {
        var proposal = CreateProposal();
        proposal.Approve(Guid.NewGuid());
        proposal.MarkAsApplied();

        var act = () => proposal.Reject(Guid.NewGuid(), "Too late");

        act.Should().Throw<DomainException>()
            .WithMessage("Cannot reject proposal in status Applied");
    }

    [Fact]
    public void Reject_ShouldThrow_WhenFailed()
    {
        var proposal = CreateProposal();
        proposal.Approve(Guid.NewGuid());
        proposal.MarkAsFailed("Failure");

        var act = () => proposal.Reject(Guid.NewGuid(), "Too late");

        act.Should().Throw<DomainException>()
            .WithMessage("Cannot reject proposal in status Failed");
    }

    [Fact]
    public void MarkAsApplied_ShouldThrow_WhenPending()
    {
        var proposal = CreateProposal();

        var act = () => proposal.MarkAsApplied();

        act.Should().Throw<DomainException>()
            .WithMessage("Only approved proposals can be marked as applied");
    }

    [Fact]
    public void MarkAsFailed_ShouldThrow_WhenPending()
    {
        var proposal = CreateProposal();

        var act = () => proposal.MarkAsFailed("Reason");

        act.Should().Throw<DomainException>()
            .WithMessage("Only approved proposals can be marked as failed");
    }

    [Fact]
    public void MarkAsFailed_ShouldThrow_WhenExpired()
    {
        var proposal = CreateProposal();
        proposal.Expire();

        var act = () => proposal.MarkAsFailed("Reason");

        act.Should().Throw<DomainException>()
            .WithMessage("Only approved proposals can be marked as failed");
    }

    [Fact]
    public void MarkAsApplied_ShouldThrow_WhenExpired()
    {
        var proposal = CreateProposal();
        proposal.Expire();

        var act = () => proposal.MarkAsApplied();

        act.Should().Throw<DomainException>()
            .WithMessage("Only approved proposals can be marked as applied");
    }

    #endregion

    #region Dismissal Edge Cases

    [Fact]
    public void Dismiss_ShouldSucceed_WhenApplied()
    {
        var proposal = CreateProposal();
        proposal.Approve(Guid.NewGuid());
        proposal.MarkAsApplied();

        proposal.Dismiss();

        proposal.Status.Should().Be(ProposalStatus.Dismissed);
    }

    [Fact]
    public void Dismiss_ShouldSucceed_WhenFailed()
    {
        var proposal = CreateProposal();
        proposal.Approve(Guid.NewGuid());
        proposal.MarkAsFailed("Something failed");

        proposal.Dismiss();

        proposal.Status.Should().Be(ProposalStatus.Dismissed);
    }

    [Fact]
    public void Dismiss_ShouldSucceed_WhenRejected()
    {
        var proposal = CreateProposal();
        proposal.Reject(Guid.NewGuid());

        proposal.Dismiss();

        proposal.Status.Should().Be(ProposalStatus.Dismissed);
    }

    [Fact]
    public void Dismiss_ShouldThrow_WhenAlreadyDismissed()
    {
        var proposal = CreateProposal();
        proposal.Expire();
        proposal.Dismiss();

        var act = () => proposal.Dismiss();

        act.Should().Throw<DomainException>()
            .WithMessage("Cannot dismiss proposal in status Dismissed");
    }

    [Fact]
    public void CanBeDismissed_ShouldBeFalse_ForPendingReview()
    {
        var proposal = CreateProposal();
        proposal.CanBeDismissed.Should().BeFalse();
    }

    [Fact]
    public void CanBeDismissed_ShouldBeFalse_ForApprovedNotExpired()
    {
        var proposal = CreateProposal();
        proposal.Approve(Guid.NewGuid());

        proposal.CanBeDismissed.Should().BeFalse();
    }

    [Fact]
    public void CanBeDismissed_ShouldBeTrue_ForApprovedAndExpired()
    {
        var proposal = CreateProposal();
        proposal.Approve(Guid.NewGuid());
        SetExpiresAt(proposal, DateTime.UtcNow.AddMinutes(-5));

        proposal.CanBeDismissed.Should().BeTrue();
    }

    [Fact]
    public void CanBeDismissed_ShouldBeTrue_WhenApplied()
    {
        var proposal = CreateProposal();
        proposal.Approve(Guid.NewGuid());
        proposal.MarkAsApplied();

        proposal.CanBeDismissed.Should().BeTrue();
    }

    [Fact]
    public void CanBeDismissed_ShouldBeTrue_WhenRejected()
    {
        var proposal = CreateProposal();
        proposal.Reject(Guid.NewGuid());

        proposal.CanBeDismissed.Should().BeTrue();
    }

    [Fact]
    public void CanBeDismissed_ShouldBeTrue_WhenFailed()
    {
        var proposal = CreateProposal();
        proposal.Approve(Guid.NewGuid());
        proposal.MarkAsFailed("Error");

        proposal.CanBeDismissed.Should().BeTrue();
    }

    [Fact]
    public void CanBeDismissed_ShouldBeTrue_WhenExpired()
    {
        var proposal = CreateProposal();
        proposal.Expire();

        proposal.CanBeDismissed.Should().BeTrue();
    }

    #endregion

    [Fact]
    public void Dismiss_ShouldThrow_WhenPendingReview()
    {
        var proposal = CreateProposal();

        var act = () => proposal.Dismiss();

        act.Should().Throw<DomainException>()
            .WithMessage("Cannot dismiss proposal in status PendingReview");
    }

    [Fact]
    public void Dismiss_ShouldThrow_WhenApprovedAndNotExpired()
    {
        var proposal = CreateProposal();
        proposal.Approve(Guid.NewGuid());

        var act = () => proposal.Dismiss();

        act.Should().Throw<DomainException>()
            .WithMessage("Cannot dismiss proposal in status Approved");
    }

    [Fact]
    public void Reject_ShouldThrow_WhenHighRisk_WithoutReason()
    {
        var proposal = CreateProposal(RiskLevel.High);

        var act = () => proposal.Reject(Guid.NewGuid());

        act.Should().Throw<DomainException>()
            .WithMessage("Rejection reason is required for High and Critical risk proposals");
    }

    [Fact]
    public void Reject_ShouldThrow_WhenCriticalRisk_WithoutReason()
    {
        var proposal = CreateProposal(RiskLevel.Critical);

        var act = () => proposal.Reject(Guid.NewGuid());

        act.Should().Throw<DomainException>()
            .WithMessage("Rejection reason is required for High and Critical risk proposals");
    }

    [Fact]
    public void AddOperation_ShouldThrow_WhenApproved()
    {
        var proposal = CreateProposal();
        proposal.Approve(Guid.NewGuid());
        var operation = CreateOperation(proposal.Id);

        var act = () => proposal.AddOperation(operation);

        act.Should().Throw<DomainException>()
            .WithMessage("Cannot add operations after proposal has been decided");
    }

        #region Operation Mutation Guards After State Transitions

    [Fact]
    public void AddOperation_ShouldThrow_WhenExpired()
    {
        var proposal = CreateProposal();
        proposal.Expire();
        var operation = CreateOperation(proposal.Id);

        var act = () => proposal.AddOperation(operation);

        act.Should().Throw<DomainException>()
            .WithMessage("Cannot add operations after proposal has been decided");
    }

    [Fact]
    public void AddOperation_ShouldThrow_WhenRejected()
    {
        var proposal = CreateProposal();
        proposal.Reject(Guid.NewGuid());
        var operation = CreateOperation(proposal.Id);

        var act = () => proposal.AddOperation(operation);

        act.Should().Throw<DomainException>()
            .WithMessage("Cannot add operations after proposal has been decided");
    }

    [Fact]
    public void AddOperation_ShouldThrow_WhenApplied()
    {
        var proposal = CreateProposal();
        proposal.Approve(Guid.NewGuid());
        proposal.MarkAsApplied();
        var operation = CreateOperation(proposal.Id);

        var act = () => proposal.AddOperation(operation);

        act.Should().Throw<DomainException>()
            .WithMessage("Cannot add operations after proposal has been decided");
    }

    [Fact]
    public void SetDiffPreview_ShouldThrow_WhenApproved()
    {
        var proposal = CreateProposal();
        proposal.Approve(Guid.NewGuid());

        var act = () => proposal.SetDiffPreview("diff");

        act.Should().Throw<DomainException>()
            .WithMessage("Cannot update diff preview after proposal has been decided");
    }

    [Fact]
    public void SetDiffPreview_ShouldThrow_WhenExpired()
    {
        var proposal = CreateProposal();
        proposal.Expire();

        var act = () => proposal.SetDiffPreview("diff");

        act.Should().Throw<DomainException>()
            .WithMessage("Cannot update diff preview after proposal has been decided");
    }

    [Fact]
    public void SetValidationIssues_ShouldThrow_WhenApproved()
    {
        var proposal = CreateProposal();
        proposal.Approve(Guid.NewGuid());

        var act = () => proposal.SetValidationIssues("issues");

        act.Should().Throw<DomainException>()
            .WithMessage("Cannot update validation issues after proposal has been decided");
    }

    [Fact]
    public void SetValidationIssues_ShouldThrow_WhenExpired()
    {
        var proposal = CreateProposal();
        proposal.Expire();

        var act = () => proposal.SetValidationIssues("issues");

        act.Should().Throw<DomainException>()
            .WithMessage("Cannot update validation issues after proposal has been decided");
    }

    #endregion

    #region Helpers

    private AutomationProposal CreateProposal(RiskLevel riskLevel = RiskLevel.Low)
    {
        return new AutomationProposal(
            ProposalSourceType.Queue,
            _userId,
            "Test proposal",
            riskLevel,
            Guid.NewGuid().ToString(),
            _boardId);
    }

    private static AutomationProposalOperation CreateOperation(Guid proposalId, int sequence = 0)
    {
        return new AutomationProposalOperation(
            proposalId,
            sequence,
            "create",
            "card",
            "{\"title\":\"Test\"}",
            Guid.NewGuid().ToString());
    }

    private static void SetExpiresAt(AutomationProposal proposal, DateTime expiresAt)
    {
        var property = typeof(AutomationProposal).GetProperty(
            nameof(AutomationProposal.ExpiresAt),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        property.Should().NotBeNull("ExpiresAt property must exist on AutomationProposal");
        property!.SetValue(proposal, expiresAt);
    }

    #endregion
}
