using FluentAssertions;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class CardEstimateTests
{
    [Fact]
    public void Estimate_DistinguishesUnknownFromZero_AndOnlyTouchesChanges()
    {
        var card = new Card(Guid.NewGuid(), Guid.NewGuid(), "Estimate me");
        card.EstimatedEffortMinutes.Should().BeNull();
        var initialVersion = card.UpdatedAt;
        card.SetEstimatedEffortMinutes(null);
        card.UpdatedAt.Should().Be(initialVersion);

        card.SetEstimatedEffortMinutes(0);
        card.EstimatedEffortMinutes.Should().Be(0);
        card.UpdatedAt.Should().BeOnOrAfter(initialVersion);
        var zeroVersion = card.UpdatedAt;
        card.SetEstimatedEffortMinutes(0);
        card.UpdatedAt.Should().Be(zeroVersion);

        card.SetEstimatedEffortMinutes(Card.MaxEstimatedEffortMinutes);
        card.EstimatedEffortMinutes.Should().Be(Card.MaxEstimatedEffortMinutes);
        card.SetEstimatedEffortMinutes(null);
        card.EstimatedEffortMinutes.Should().BeNull();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1_000_001)]
    [InlineData(int.MaxValue)]
    public void InvalidEstimate_PreservesPreviousValueAndVersion(int value)
    {
        var card = new Card(Guid.NewGuid(), Guid.NewGuid(), "Keep estimate");
        card.SetEstimatedEffortMinutes(90);
        var version = card.UpdatedAt;

        Action act = () => card.SetEstimatedEffortMinutes(value);

        act.Should().Throw<DomainException>().Which.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        card.EstimatedEffortMinutes.Should().Be(90);
        card.UpdatedAt.Should().Be(version);
    }

    [Fact]
    public void ArchivedCard_RejectsEstimateWrites_AndRetainsEstimateOnRestore()
    {
        var card = new Card(Guid.NewGuid(), Guid.NewGuid(), "Keep estimate");
        card.SetEstimatedEffortMinutes(90);
        card.Archive();
        var version = card.UpdatedAt;

        foreach (var value in new int?[] { null, 0, 90 })
        {
            Action act = () => card.SetEstimatedEffortMinutes(value);
            act.Should().Throw<DomainException>().Which.ErrorCode.Should().Be(ErrorCodes.InvalidOperation);
        }

        card.UpdatedAt.Should().Be(version);
        card.Restore();
        card.EstimatedEffortMinutes.Should().Be(90);
    }
}
