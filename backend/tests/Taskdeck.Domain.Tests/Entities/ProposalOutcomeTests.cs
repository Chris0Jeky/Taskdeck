using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class ProposalOutcomeTests
{
    private readonly Guid _proposalId = Guid.NewGuid();
    private readonly Guid _decidedByUserId = Guid.NewGuid();

    [Theory]
    [InlineData(OutcomeType.Approved)]
    [InlineData(OutcomeType.EditedThenApproved)]
    [InlineData(OutcomeType.Rejected)]
    [InlineData(OutcomeType.Ignored)]
    public void Constructor_ShouldCreateOutcome_ForEachValidOutcomeType(OutcomeType outcomeType)
    {
        var before = DateTime.UtcNow;

        var outcome = new ProposalOutcome(_proposalId, outcomeType, _decidedByUserId);

        outcome.Id.Should().NotBe(Guid.Empty);
        outcome.ProposalId.Should().Be(_proposalId);
        outcome.OutcomeType.Should().Be(outcomeType);
        outcome.DecidedByUserId.Should().Be(_decidedByUserId);
        outcome.DecidedAt.Should().BeOnOrAfter(before);
        outcome.DecidedAt.Should().BeOnOrBefore(DateTime.UtcNow);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenProposalIdIsEmpty()
    {
        var act = () => new ProposalOutcome(Guid.Empty, OutcomeType.Approved, _decidedByUserId);

        act.Should().Throw<DomainException>()
            .WithMessage("ProposalId cannot be empty");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenDecidedByUserIdIsEmpty()
    {
        var act = () => new ProposalOutcome(_proposalId, OutcomeType.Approved, Guid.Empty);

        act.Should().Throw<DomainException>()
            .WithMessage("DecidedByUserId cannot be empty");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenOutcomeTypeIsInvalid()
    {
        var act = () => new ProposalOutcome(_proposalId, (OutcomeType)999, _decidedByUserId);

        act.Should().Throw<DomainException>()
            .WithMessage("Invalid OutcomeType: 999");
    }

    [Fact]
    public void OutcomeType_Enum_HasExpectedValues()
    {
        // Verify the enum has exactly the expected members
        var values = Enum.GetValues<OutcomeType>();
        values.Should().HaveCount(4);
        values.Should().Contain(OutcomeType.Approved);
        values.Should().Contain(OutcomeType.EditedThenApproved);
        values.Should().Contain(OutcomeType.Rejected);
        values.Should().Contain(OutcomeType.Ignored);
    }
}
