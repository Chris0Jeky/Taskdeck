using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public sealed class ThinkingDeckTests
{
    [Fact]
    public void RejectsDuplicateIdsWithoutChangingSavedState()
    {
        var deck = new ThinkingDeck(Guid.NewGuid());
        var layer = new ThinkingLayer(Guid.NewGuid(), "note", "A note", "", []);
        deck.Replace([layer]);
        var act = () => deck.Replace([layer, layer]);
        act.Should().Throw<DomainException>();
        deck.Revision.Should().Be(1);
        deck.ReadLayers().Should().HaveCount(1);
    }

    [Fact]
    public void RejectsOversizedPayloadAndNonStepCompletion()
    {
        var deck = new ThinkingDeck(Guid.NewGuid());
        var oversized = () => deck.Replace([new(Guid.NewGuid(), "note", "", new string('x', 8001), [])]);
        oversized.Should().Throw<DomainException>();
        var invalidCompletion = () => deck.Replace([new(Guid.NewGuid(), "options", "", "", [new(Guid.NewGuid(), "Option", true)])]);
        invalidCompletion.Should().Throw<DomainException>();
        deck.Revision.Should().Be(0);
    }
}
