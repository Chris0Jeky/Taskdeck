using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class ProposalOutcomeTests
{
    private readonly Guid _proposalId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    private ProposalOutcome CreateValidOutcome(
        OutcomeDecision decision = OutcomeDecision.Approved,
        int editedFieldCount = 0,
        double? averageFieldConfidence = null)
    {
        return new ProposalOutcome(
            _proposalId,
            _userId,
            decision,
            decisionLatencySeconds: 12.5,
            fieldCount: 3,
            editedFieldCount: editedFieldCount,
            sourceType: "Queue",
            riskLevel: "Low",
            modelId: "gpt-4o",
            averageFieldConfidence: averageFieldConfidence);
    }

    [Fact]
    public void Constructor_ShouldCreateOutcome_WithValidData()
    {
        var outcome = CreateValidOutcome();

        outcome.ProposalId.Should().Be(_proposalId);
        outcome.DecidedByUserId.Should().Be(_userId);
        outcome.Decision.Should().Be(OutcomeDecision.Approved);
        outcome.DecisionLatencySeconds.Should().Be(12.5);
        outcome.FieldCount.Should().Be(3);
        outcome.EditedFieldCount.Should().Be(0);
        outcome.SourceType.Should().Be("Queue");
        outcome.RiskLevel.Should().Be("Low");
        outcome.ModelId.Should().Be("gpt-4o");
        outcome.AverageFieldConfidence.Should().BeNull();
        outcome.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Constructor_ShouldAccept_EditedThenApprovedWithEditCount()
    {
        var outcome = new ProposalOutcome(
            _proposalId, _userId, OutcomeDecision.EditedThenApproved,
            5.0, 4, 2, "Chat", "Medium", "mock", 0.85);

        outcome.EditedFieldCount.Should().Be(2);
        outcome.OutcomeType.Should().Be(OutcomeType.EditedThenApproved);
        outcome.AverageFieldConfidence.Should().Be(0.85);
    }

    [Theory]
    [InlineData(OutcomeType.Approved)]
    [InlineData(OutcomeType.EditedThenApproved)]
    [InlineData(OutcomeType.Rejected)]
    [InlineData(OutcomeType.Ignored)]
    public void Constructor_ShouldCreateOutcome_ForEachValidOutcomeType(OutcomeType outcomeType)
    {
        var before = DateTimeOffset.UtcNow;

        var outcome = new ProposalOutcome(_proposalId, outcomeType, _userId);

        outcome.Id.Should().NotBe(Guid.Empty);
        outcome.ProposalId.Should().Be(_proposalId);
        outcome.OutcomeType.Should().Be(outcomeType);
        outcome.DecidedByUserId.Should().Be(_userId);
        outcome.DecidedAt.Should().BeOnOrAfter(before);
        outcome.DecidedAt.Should().BeOnOrBefore(DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenProposalIdIsEmpty()
    {
        var act = () => new ProposalOutcome(
            Guid.Empty, _userId, OutcomeDecision.Approved,
            1.0, 1, 0, "Queue", "Low");

        act.Should().Throw<DomainException>()
            .WithMessage("ProposalId cannot be empty");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenUserIdIsEmpty()
    {
        var act = () => new ProposalOutcome(
            _proposalId, Guid.Empty, OutcomeDecision.Approved,
            1.0, 1, 0, "Queue", "Low");

        act.Should().Throw<DomainException>()
            .WithMessage("DecidedByUserId cannot be empty");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenDecisionIsInvalid()
    {
        var act = () => new ProposalOutcome(
            _proposalId, _userId, (OutcomeDecision)99,
            1.0, 1, 0, "Queue", "Low");

        act.Should().Throw<DomainException>()
            .WithMessage("OutcomeDecision value is invalid");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenOutcomeTypeIsInvalid()
    {
        var act = () => new ProposalOutcome(_proposalId, (OutcomeType)999, _userId);

        act.Should().Throw<DomainException>()
            .WithMessage("Invalid OutcomeType: 999");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenLatencyIsNegative()
    {
        var act = () => new ProposalOutcome(
            _proposalId, _userId, OutcomeDecision.Approved,
            -1.0, 1, 0, "Queue", "Low");

        act.Should().Throw<DomainException>()
            .WithMessage("DecisionLatencySeconds cannot be negative or non-finite");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenFieldCountIsNegative()
    {
        var act = () => new ProposalOutcome(
            _proposalId, _userId, OutcomeDecision.Approved,
            1.0, -1, 0, "Queue", "Low");

        act.Should().Throw<DomainException>()
            .WithMessage("FieldCount cannot be negative");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenEditedFieldCountIsNegative()
    {
        var act = () => new ProposalOutcome(
            _proposalId, _userId, OutcomeDecision.EditedThenApproved,
            1.0, 3, -1, "Queue", "Low");

        act.Should().Throw<DomainException>()
            .WithMessage("EditedFieldCount cannot be negative");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenEditedFieldCountExceedsFieldCount()
    {
        var act = () => new ProposalOutcome(
            _proposalId, _userId, OutcomeDecision.EditedThenApproved,
            1.0, 2, 3, "Queue", "Low");

        act.Should().Throw<DomainException>()
            .WithMessage("EditedFieldCount cannot exceed FieldCount");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenSourceTypeIsEmpty()
    {
        var act = () => new ProposalOutcome(
            _proposalId, _userId, OutcomeDecision.Approved,
            1.0, 1, 0, "", "Low");

        act.Should().Throw<DomainException>()
            .WithMessage("SourceType cannot be empty");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenSourceTypeExceedsMaxLength()
    {
        var act = () => new ProposalOutcome(
            _proposalId, _userId, OutcomeDecision.Approved,
            1.0, 1, 0, new string('x', 51), "Low");

        act.Should().Throw<DomainException>()
            .WithMessage("SourceType cannot exceed 50 characters");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenRiskLevelIsEmpty()
    {
        var act = () => new ProposalOutcome(
            _proposalId, _userId, OutcomeDecision.Approved,
            1.0, 1, 0, "Queue", "");

        act.Should().Throw<DomainException>()
            .WithMessage("RiskLevel cannot be empty");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenModelIdExceedsMaxLength()
    {
        var act = () => new ProposalOutcome(
            _proposalId, _userId, OutcomeDecision.Approved,
            1.0, 1, 0, "Queue", "Low", modelId: new string('m', 101));

        act.Should().Throw<DomainException>()
            .WithMessage("ModelId cannot exceed 100 characters");
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Constructor_ShouldThrow_WhenAverageFieldConfidenceOutOfRange(double confidence)
    {
        var act = () => new ProposalOutcome(
            _proposalId, _userId, OutcomeDecision.Approved,
            1.0, 1, 0, "Queue", "Low", averageFieldConfidence: confidence);

        act.Should().Throw<DomainException>()
            .WithMessage("AverageFieldConfidence must be between 0.0 and 1.0");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenEditedFieldCountNonZero_ForNonEditDecision()
    {
        // Approved with editedFieldCount > 0 should fail
        var act = () => new ProposalOutcome(
            _proposalId, _userId, OutcomeDecision.Approved,
            1.0, 3, 1, "Queue", "Low");

        act.Should().Throw<DomainException>()
            .WithMessage("EditedFieldCount must be 0 when decision is not EditedThenApproved");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenEditedThenApprovedHasNoEdits()
    {
        var act = () => new ProposalOutcome(
            _proposalId, _userId, OutcomeDecision.EditedThenApproved,
            1.0, 3, 0, "Queue", "Low");

        act.Should().Throw<DomainException>()
            .WithMessage("EditedFieldCount must be greater than 0 when decision is EditedThenApproved");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenRejectedWithEditedFieldCount()
    {
        var act = () => new ProposalOutcome(
            _proposalId, _userId, OutcomeDecision.Rejected,
            1.0, 3, 1, "Queue", "Low");

        act.Should().Throw<DomainException>()
            .WithMessage("EditedFieldCount must be 0 when decision is not EditedThenApproved");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenIgnoredWithEditedFieldCount()
    {
        var act = () => new ProposalOutcome(
            _proposalId, _userId, OutcomeDecision.Ignored,
            1.0, 3, 1, "Queue", "Low");

        act.Should().Throw<DomainException>()
            .WithMessage("EditedFieldCount must be 0 when decision is not EditedThenApproved");
    }

    [Fact]
    public void Constructor_ContentFree_ShouldNotStoreAnyUserContent()
    {
        // This test validates the design intent: ProposalOutcome stores
        // only structural/dimensional data, never proposal text or user content.
        var outcome = CreateValidOutcome();

        // Verify that no property contains or could contain user-generated content
        // Only IDs, enums, numeric dimensions, and short type labels are stored
        outcome.SourceType.Should().NotContain("user text");
        outcome.RiskLevel.Should().NotContain("user text");

        // The entity has no property for proposal text, summary, or description
        var properties = typeof(ProposalOutcome).GetProperties();
        var propertyNames = properties.Select(p => p.Name).ToList();

        propertyNames.Should().NotContain("Summary");
        propertyNames.Should().NotContain("Description");
        propertyNames.Should().NotContain("Content");
        propertyNames.Should().NotContain("Text");
        propertyNames.Should().NotContain("Body");
    }

    [Fact]
    public void Constructor_ShouldAcceptZeroLatency()
    {
        var outcome = new ProposalOutcome(
            _proposalId, _userId, OutcomeDecision.Approved,
            0.0, 1, 0, "Queue", "Low");

        outcome.DecisionLatencySeconds.Should().Be(0.0);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenLatencyIsNaN()
    {
        var act = () => new ProposalOutcome(
            _proposalId, _userId, OutcomeDecision.Approved,
            double.NaN, 1, 0, "Queue", "Low");

        act.Should().Throw<DomainException>()
            .WithMessage("DecisionLatencySeconds cannot be negative or non-finite");
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Constructor_ShouldThrow_WhenLatencyIsNonFinite(double latency)
    {
        var act = () => new ProposalOutcome(
            _proposalId, _userId, OutcomeDecision.Approved,
            latency, 1, 0, "Queue", "Low");

        act.Should().Throw<DomainException>()
            .WithMessage("DecisionLatencySeconds cannot be negative or non-finite");
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Constructor_ShouldThrow_WhenAverageFieldConfidenceIsNonFinite(double confidence)
    {
        var act = () => new ProposalOutcome(
            _proposalId, _userId, OutcomeDecision.Approved,
            1.0, 1, 0, "Queue", "Low", averageFieldConfidence: confidence);

        act.Should().Throw<DomainException>()
            .WithMessage("AverageFieldConfidence must be between 0.0 and 1.0");
    }

    [Fact]
    public void OutcomeType_Enum_HasExpectedValues()
    {
        var values = Enum.GetValues<OutcomeType>();
        values.Should().HaveCount(4);
        values.Should().Contain(OutcomeType.Approved);
        values.Should().Contain(OutcomeType.EditedThenApproved);
        values.Should().Contain(OutcomeType.Rejected);
        values.Should().Contain(OutcomeType.Ignored);
    }
}
