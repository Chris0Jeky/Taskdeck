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

    private AutomationProposal CreateProposal(RiskLevel riskLevel = RiskLevel.Medium)
    {
        return new AutomationProposal(
            ProposalSourceType.Queue,
            _requestedByUserId,
            "Create a task",
            riskLevel,
            "corr-test",
            _boardId);
    }

    private static void SetPrivateDateTime(object target, string propertyName, DateTime value)
    {
        var property = target.GetType().GetProperty(propertyName);
        property.Should().NotBeNull();
        property!.SetValue(target, value);
    }
}
