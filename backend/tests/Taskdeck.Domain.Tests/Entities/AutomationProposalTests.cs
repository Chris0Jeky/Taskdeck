using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class AutomationProposalTests
{
    private readonly Guid _requestedByUserId = Guid.NewGuid();
    private readonly Guid _boardId = Guid.NewGuid();

    [Fact]
    public void Constructor_ShouldCreateProposal_WithValidData()
    {
        // Arrange & Act
        var before = DateTime.UtcNow;
        var proposal = new AutomationProposal(
            ProposalSourceType.Chat,
            _requestedByUserId,
            "Create a follow-up card",
            RiskLevel.Medium,
            "corr-123",
            _boardId,
            "chat-42",
            expiryMinutes: 30);

        // Assert
        proposal.SourceType.Should().Be(ProposalSourceType.Chat);
        proposal.RequestedByUserId.Should().Be(_requestedByUserId);
        proposal.Summary.Should().Be("Create a follow-up card");
        proposal.RiskLevel.Should().Be(RiskLevel.Medium);
        proposal.CorrelationId.Should().Be("corr-123");
        proposal.BoardId.Should().Be(_boardId);
        proposal.SourceReferenceId.Should().Be("chat-42");
        proposal.ExpiresAt.Should().BeAfter(before);
        proposal.ExpiresAt.Should().BeOnOrBefore(before.AddMinutes(30).AddSeconds(1));
    }

    [Fact]
    public void Constructor_ShouldInitializeLifecycleDefaults()
    {
        // Arrange & Act
        var proposal = new AutomationProposal(
            ProposalSourceType.Queue,
            _requestedByUserId,
            "Update board labels",
            RiskLevel.Low,
            "corr-456");

        // Assert
        proposal.Id.Should().NotBe(Guid.Empty);
        proposal.Status.Should().Be(ProposalStatus.PendingReview);
        proposal.DecidedAt.Should().BeNull();
        proposal.DecidedByUserId.Should().BeNull();
        proposal.AppliedAt.Should().BeNull();
        proposal.FailureReason.Should().BeNull();
        proposal.DiffPreview.Should().BeNull();
        proposal.ValidationIssues.Should().BeNull();
        proposal.Operations.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenRequestedByUserIdIsEmpty()
    {
        // Act
        var act = () => new AutomationProposal(
            ProposalSourceType.Manual,
            Guid.Empty,
            "Summary",
            RiskLevel.Low,
            "corr-1");

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("RequestedByUserId cannot be empty");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Constructor_ShouldThrow_WhenSummaryIsBlank(string summary)
    {
        // Act
        var act = () => new AutomationProposal(
            ProposalSourceType.Manual,
            _requestedByUserId,
            summary,
            RiskLevel.Low,
            "corr-1");

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Summary cannot be empty");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenSummaryIsTooLong()
    {
        // Arrange
        var summary = new string('a', 501);

        // Act
        var act = () => new AutomationProposal(
            ProposalSourceType.Manual,
            _requestedByUserId,
            summary,
            RiskLevel.Low,
            "corr-1");

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Summary cannot exceed 500 characters");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Constructor_ShouldThrow_WhenCorrelationIdIsBlank(string correlationId)
    {
        // Act
        var act = () => new AutomationProposal(
            ProposalSourceType.Manual,
            _requestedByUserId,
            "Summary",
            RiskLevel.Low,
            correlationId);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("CorrelationId cannot be empty");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_ShouldThrow_WhenExpiryMinutesIsNotPositive(int expiryMinutes)
    {
        // Act
        var act = () => new AutomationProposal(
            ProposalSourceType.Manual,
            _requestedByUserId,
            "Summary",
            RiskLevel.Low,
            "corr-1",
            expiryMinutes: expiryMinutes);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("ExpiryMinutes must be positive");
    }

    [Fact]
    public void AddOperation_ShouldAddOperation_WhenPendingReview()
    {
        // Arrange
        var proposal = CreateProposal();
        var operation = new AutomationProposalOperation(
            proposal.Id,
            0,
            "create",
            "card",
            "{\"title\":\"Task\"}",
            "idem-1");
        var originalUpdatedAt = proposal.UpdatedAt;

        // Act
        proposal.AddOperation(operation);

        // Assert
        proposal.Operations.Should().ContainSingle()
            .Which.Should().Be(operation);
        proposal.UpdatedAt.Should().BeOnOrAfter(originalUpdatedAt);
    }

    [Fact]
    public void SetDiffPreviewAndValidationIssues_ShouldUpdateFields_WhenPendingReview()
    {
        // Arrange
        var proposal = CreateProposal();
        var originalUpdatedAt = proposal.UpdatedAt;

        // Act
        proposal.SetDiffPreview("diff preview");
        proposal.SetValidationIssues("validation issues");

        // Assert
        proposal.DiffPreview.Should().Be("diff preview");
        proposal.ValidationIssues.Should().Be("validation issues");
        proposal.UpdatedAt.Should().BeOnOrAfter(originalUpdatedAt);
    }

    [Fact]
    public void Approve_ShouldSetApprovedState()
    {
        // Arrange
        var proposal = CreateProposal();
        var decidedByUserId = Guid.NewGuid();
        var originalUpdatedAt = proposal.UpdatedAt;

        // Act
        proposal.Approve(decidedByUserId);

        // Assert
        proposal.Status.Should().Be(ProposalStatus.Approved);
        proposal.DecidedByUserId.Should().Be(decidedByUserId);
        proposal.DecidedAt.Should().NotBeNull();
        proposal.UpdatedAt.Should().BeOnOrAfter(originalUpdatedAt);
    }

    [Fact]
    public void Approve_ShouldThrow_WhenProposalIsExpired()
    {
        // Arrange
        var proposal = CreateProposal();
        SetPrivateDateTime(proposal, "ExpiresAt", DateTime.UtcNow.AddMinutes(-1));

        // Act
        var act = () => proposal.Approve(Guid.NewGuid());

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Cannot approve expired proposal");
    }

    [Fact]
    public void Reject_ShouldSetRejectedState_AndFailureReason()
    {
        // Arrange
        var proposal = CreateProposal(riskLevel: RiskLevel.Low);
        var decidedByUserId = Guid.NewGuid();

        // Act
        proposal.Reject(decidedByUserId, "Not needed");

        // Assert
        proposal.Status.Should().Be(ProposalStatus.Rejected);
        proposal.DecidedByUserId.Should().Be(decidedByUserId);
        proposal.DecidedAt.Should().NotBeNull();
        proposal.FailureReason.Should().Be("Not needed");
    }

    [Fact]
    public void Reject_ShouldThrow_WhenHighRiskReasonIsMissing()
    {
        // Arrange
        var proposal = CreateProposal(riskLevel: RiskLevel.High);

        // Act
        var act = () => proposal.Reject(Guid.NewGuid());

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Rejection reason is required for High and Critical risk proposals");
    }

    [Fact]
    public void MarkAsApplied_ShouldSetAppliedState_WhenApproved()
    {
        // Arrange
        var proposal = CreateProposal();
        proposal.Approve(Guid.NewGuid());
        var approvedUpdatedAt = proposal.UpdatedAt;

        // Act
        proposal.MarkAsApplied();

        // Assert
        proposal.Status.Should().Be(ProposalStatus.Applied);
        proposal.AppliedAt.Should().NotBeNull();
        proposal.UpdatedAt.Should().BeOnOrAfter(approvedUpdatedAt);
    }

    [Fact]
    public void MarkAsFailed_ShouldSetFailedState_WhenApproved()
    {
        // Arrange
        var proposal = CreateProposal();
        proposal.Approve(Guid.NewGuid());
        var approvedUpdatedAt = proposal.UpdatedAt;

        // Act
        proposal.MarkAsFailed("Execution conflict");

        // Assert
        proposal.Status.Should().Be(ProposalStatus.Failed);
        proposal.FailureReason.Should().Be("Execution conflict");
        proposal.UpdatedAt.Should().BeOnOrAfter(approvedUpdatedAt);
    }

    [Fact]
    public void Expire_ShouldSetExpiredState_WhenPendingReview()
    {
        // Arrange
        var proposal = CreateProposal();
        var originalUpdatedAt = proposal.UpdatedAt;

        // Act
        proposal.Expire();

        // Assert
        proposal.Status.Should().Be(ProposalStatus.Expired);
        proposal.UpdatedAt.Should().BeOnOrAfter(originalUpdatedAt);
    }

    [Fact]
    public void AddOperation_ShouldThrow_WhenProposalHasBeenDecided()
    {
        // Arrange
        var proposal = CreateProposal();
        proposal.Approve(Guid.NewGuid());
        var operation = new AutomationProposalOperation(
            proposal.Id,
            0,
            "create",
            "card",
            "{\"title\":\"Task\"}",
            "idem-1");

        // Act
        var act = () => proposal.AddOperation(operation);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Cannot add operations after proposal has been decided");
    }

    [Fact]
    public void MarkAsApplied_ShouldThrow_WhenProposalIsNotApproved()
    {
        // Arrange
        var proposal = CreateProposal();

        // Act
        var act = () => proposal.MarkAsApplied();

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Only approved proposals can be marked as applied");
    }

    [Fact]
    public void SetDiffPreview_ShouldThrow_WhenProposalHasBeenDecided()
    {
        // Arrange
        var proposal = CreateProposal();
        proposal.Reject(Guid.NewGuid(), "Not needed");

        // Act
        var act = () => proposal.SetDiffPreview("diff preview");

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Cannot update diff preview after proposal has been decided");
    }

    [Fact]
    public void SetValidationIssues_ShouldThrow_WhenProposalHasBeenDecided()
    {
        // Arrange
        var proposal = CreateProposal();
        proposal.Reject(Guid.NewGuid(), "Not needed");

        // Act
        var act = () => proposal.SetValidationIssues("issues");

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Cannot update validation issues after proposal has been decided");
    }

    [Fact]
    public void Approve_ShouldThrow_WhenDecidedByUserIdIsEmpty()
    {
        // Arrange
        var proposal = CreateProposal();

        // Act
        var act = () => proposal.Approve(Guid.Empty);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("DecidedByUserId cannot be empty");
    }

    [Fact]
    public void Reject_ShouldThrow_WhenDecidedByUserIdIsEmpty()
    {
        // Arrange
        var proposal = CreateProposal();

        // Act
        var act = () => proposal.Reject(Guid.Empty, "reason");

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("DecidedByUserId cannot be empty");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void MarkAsFailed_ShouldThrow_WhenFailureReasonIsBlank(string reason)
    {
        // Arrange
        var proposal = CreateProposal();
        proposal.Approve(Guid.NewGuid());

        // Act
        var act = () => proposal.MarkAsFailed(reason);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("FailureReason cannot be empty");
    }

    [Fact]
    public void Approve_ShouldThrow_WhenProposalIsAlreadyRejected()
    {
        // Arrange
        var proposal = CreateProposal();
        proposal.Reject(Guid.NewGuid(), "Not needed");

        // Act
        var act = () => proposal.Approve(Guid.NewGuid());

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Cannot approve proposal in status Rejected");
    }

    [Fact]
    public void Reject_ShouldThrow_WhenProposalIsAlreadyApproved()
    {
        // Arrange
        var proposal = CreateProposal();
        proposal.Approve(Guid.NewGuid());

        // Act
        var act = () => proposal.Reject(Guid.NewGuid(), "Too late");

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Cannot reject proposal in status Approved");
    }

    [Fact]
    public void Reject_ShouldThrow_WhenCriticalRiskReasonIsMissing()
    {
        // Arrange
        var proposal = CreateProposal(riskLevel: RiskLevel.Critical);

        // Act
        var act = () => proposal.Reject(Guid.NewGuid());

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Rejection reason is required for High and Critical risk proposals");
    }

    [Fact]
    public void Constructor_ShouldAcceptSummary_AtExactMaxLength()
    {
        // Arrange
        var summary = new string('a', 500);

        // Act
        var proposal = new AutomationProposal(
            ProposalSourceType.Manual,
            _requestedByUserId,
            summary,
            RiskLevel.Low,
            "corr-1");

        // Assert
        proposal.Summary.Should().HaveLength(500);
    }

    [Fact]
    public void Dismiss_ShouldSucceed_WhenApprovedAndExpired()
    {
        // Arrange
        var proposal = CreateProposal();
        proposal.Approve(Guid.NewGuid());
        SetPrivateDateTime(proposal, "ExpiresAt", DateTime.UtcNow.AddMinutes(-5));

        // Act
        proposal.Dismiss();

        // Assert
        proposal.Status.Should().Be(ProposalStatus.Dismissed);
    }

    [Fact]
    public void Dismiss_ShouldThrow_WhenApprovedAndNotExpired()
    {
        // Arrange
        var proposal = CreateProposal();
        proposal.Approve(Guid.NewGuid());

        // Act
        var act = () => proposal.Dismiss();

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Cannot dismiss proposal in status Approved");
    }

    [Fact]
    public void Dismiss_ShouldSucceed_WhenStatusIsExpired()
    {
        // Arrange
        var proposal = CreateProposal();
        proposal.Expire();

        // Act
        proposal.Dismiss();

        // Assert
        proposal.Status.Should().Be(ProposalStatus.Dismissed);
    }

    [Fact]
    public void Dismiss_ShouldThrow_WhenPendingReview()
    {
        // Arrange
        var proposal = CreateProposal();

        // Act
        var act = () => proposal.Dismiss();

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Cannot dismiss proposal in status PendingReview");
    }

    // --- Defer (snooze) ---------------------------------------------------

    [Fact]
    public void Defer_ShouldSetDeferredUntil_AndKeepPendingReview()
    {
        // Arrange
        var proposal = CreateProposal();
        var originalUpdatedAt = proposal.UpdatedAt;
        var before = DateTime.UtcNow;

        // Act
        proposal.Defer(TimeSpan.FromMinutes(60));

        // Assert
        proposal.Status.Should().Be(ProposalStatus.PendingReview);
        proposal.DeferredUntil.Should().NotBeNull();
        proposal.DeferredUntil!.Value.Should().BeOnOrAfter(before.AddMinutes(60));
        proposal.DeferredUntil!.Value.Should().BeOnOrBefore(DateTime.UtcNow.AddMinutes(60).AddSeconds(1));
        proposal.IsDeferred.Should().BeTrue();
        proposal.DecidedByUserId.Should().BeNull();
        proposal.DecidedAt.Should().BeNull();
        proposal.UpdatedAt.Should().BeOnOrAfter(originalUpdatedAt);
    }

    [Fact]
    public void Defer_ShouldPushExpiresAtBeyondDeferredUntil_ForNearExpiryProposal()
    {
        // Arrange: a proposal that would otherwise expire in 10 minutes.
        var proposal = CreateProposal(expiryMinutes: 10);

        // Act
        proposal.Defer(TimeSpan.FromMinutes(60));

        // Assert: ExpiresAt is pushed strictly beyond DeferredUntil (+ grace), so the
        // snoozed proposal cannot silently expire and can still be approved.
        proposal.ExpiresAt.Should().BeAfter(proposal.DeferredUntil!.Value);
        proposal.ExpiresAt.Should().BeOnOrAfter(proposal.DeferredUntil!.Value + AutomationProposal.DeferExpiryGrace);
        proposal.IsExpired.Should().BeFalse();
        var approve = () => proposal.Approve(Guid.NewGuid());
        approve.Should().NotThrow();
    }

    [Fact]
    public void Defer_ShouldNotShortenExpiresAt_WhenAlreadyBeyondDeferFloor()
    {
        // Arrange: an expiry (26h) already beyond defer(60min) + 24h grace (= 25h),
        // so the floor cannot push it out.
        var proposal = CreateProposal(expiryMinutes: 1560);
        var originalExpiresAt = proposal.ExpiresAt;

        // Act
        proposal.Defer(TimeSpan.FromMinutes(60));

        // Assert: the far-future ExpiresAt is preserved (never pulled in or pushed out).
        proposal.ExpiresAt.Should().Be(originalExpiresAt);
    }

    [Fact]
    public void Defer_ShouldUpdateInPlace_OnRepeatedCalls_NotStack()
    {
        // Arrange
        var proposal = CreateProposal();
        proposal.Defer(TimeSpan.FromMinutes(30));

        // Act: re-snooze with a longer window.
        var before = DateTime.UtcNow;
        proposal.Defer(TimeSpan.FromMinutes(60));

        // Assert: the window resets to the latest defer (not 30+60).
        proposal.DeferredUntil!.Value.Should().BeOnOrAfter(before.AddMinutes(60));
        proposal.DeferredUntil!.Value.Should().BeOnOrBefore(DateTime.UtcNow.AddMinutes(60).AddSeconds(1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(AutomationProposal.MaxDeferMinutes + 1)]
    [InlineData(100000)]
    public void Defer_ShouldThrowValidationError_WhenDurationOutOfRange(int minutes)
    {
        // Arrange
        var proposal = CreateProposal();

        // Act
        var act = () => proposal.Defer(TimeSpan.FromMinutes(minutes));

        // Assert
        act.Should().Throw<DomainException>()
            .Where(ex => ex.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Defer_ShouldThrowInvalidOperation_WhenProposalIsExpired()
    {
        // Arrange
        var proposal = CreateProposal();
        SetPrivateDateTime(proposal, "ExpiresAt", DateTime.UtcNow.AddMinutes(-1));

        // Act
        var act = () => proposal.Defer(TimeSpan.FromMinutes(60));

        // Assert
        act.Should().Throw<DomainException>()
            .Where(ex => ex.ErrorCode == ErrorCodes.InvalidOperation)
            .WithMessage("Cannot defer expired proposal");
    }

    [Theory]
    [InlineData(ProposalStatus.Approved)]
    [InlineData(ProposalStatus.Rejected)]
    [InlineData(ProposalStatus.Applied)]
    [InlineData(ProposalStatus.Failed)]
    [InlineData(ProposalStatus.Expired)]
    [InlineData(ProposalStatus.Dismissed)]
    public void Defer_ShouldThrowInvalidOperation_WhenNotPendingReview(ProposalStatus status)
    {
        // Arrange
        var proposal = CreateProposalInStatus(status);

        // Act
        var act = () => proposal.Defer(TimeSpan.FromMinutes(60));

        // Assert
        act.Should().Throw<DomainException>()
            .Where(ex => ex.ErrorCode == ErrorCodes.InvalidOperation);
    }

    [Fact]
    public void Approve_ShouldClearDeferredUntil()
    {
        var proposal = CreateProposal();
        proposal.Defer(TimeSpan.FromMinutes(60));
        proposal.DeferredUntil.Should().NotBeNull();

        proposal.Approve(Guid.NewGuid());

        proposal.DeferredUntil.Should().BeNull();
    }

    [Fact]
    public void Reject_ShouldClearDeferredUntil()
    {
        var proposal = CreateProposal(riskLevel: RiskLevel.Low);
        proposal.Defer(TimeSpan.FromMinutes(60));

        proposal.Reject(Guid.NewGuid(), "Not needed");

        proposal.DeferredUntil.Should().BeNull();
    }

    [Fact]
    public void Expire_ShouldClearDeferredUntil()
    {
        var proposal = CreateProposal();
        proposal.Defer(TimeSpan.FromMinutes(60));

        proposal.Expire();

        proposal.DeferredUntil.Should().BeNull();
    }

    [Fact]
    public void Dismiss_ShouldClearDeferredUntil()
    {
        var proposal = CreateProposal();
        proposal.Defer(TimeSpan.FromMinutes(60));
        proposal.Expire();
        // Re-confirm Expire cleared it, then dismiss from the terminal state.
        proposal.DeferredUntil.Should().BeNull();

        proposal.Dismiss();

        proposal.DeferredUntil.Should().BeNull();
    }

    [Fact]
    public void MarkAsApplied_ShouldClearDeferredUntil()
    {
        // Arrange: defer, then approve (which clears it), then re-set a residual snooze
        // via reflection to prove MarkAsApplied also clears it defensively.
        var proposal = CreateProposal();
        proposal.Approve(Guid.NewGuid());
        var property = typeof(AutomationProposal).GetProperty("DeferredUntil");
        property!.SetValue(proposal, DateTime.UtcNow.AddMinutes(60));
        proposal.DeferredUntil.Should().NotBeNull();

        // Act
        proposal.MarkAsApplied();

        // Assert
        proposal.DeferredUntil.Should().BeNull();
    }

    private AutomationProposal CreateProposal(RiskLevel riskLevel = RiskLevel.Medium, int expiryMinutes = 1440)
    {
        return new AutomationProposal(
            ProposalSourceType.Queue,
            _requestedByUserId,
            "Create a task",
            riskLevel,
            "corr-test",
            _boardId,
            expiryMinutes: expiryMinutes);
    }

    private AutomationProposal CreateProposalInStatus(ProposalStatus status)
    {
        var proposal = CreateProposal(riskLevel: RiskLevel.Low);
        switch (status)
        {
            case ProposalStatus.PendingReview:
                break;
            case ProposalStatus.Approved:
                proposal.Approve(Guid.NewGuid());
                break;
            case ProposalStatus.Rejected:
                proposal.Reject(Guid.NewGuid(), "Not needed");
                break;
            case ProposalStatus.Applied:
                proposal.Approve(Guid.NewGuid());
                proposal.MarkAsApplied();
                break;
            case ProposalStatus.Failed:
                proposal.Approve(Guid.NewGuid());
                proposal.MarkAsFailed("boom");
                break;
            case ProposalStatus.Expired:
                proposal.Expire();
                break;
            case ProposalStatus.Dismissed:
                proposal.Expire();
                proposal.Dismiss();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, null);
        }

        return proposal;
    }

    private static void SetPrivateDateTime(object target, string propertyName, DateTime value)
    {
        var property = target.GetType().GetProperty(propertyName);
        property.Should().NotBeNull();
        property!.SetValue(target, value);
    }
}
