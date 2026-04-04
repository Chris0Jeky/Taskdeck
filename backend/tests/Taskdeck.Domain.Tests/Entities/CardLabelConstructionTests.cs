using FluentAssertions;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class CardLabelConstructionTests
{
    [Fact]
    public void Constructor_ValidIds_SetsProperties()
    {
        var cardId = Guid.NewGuid();
        var labelId = Guid.NewGuid();

        var cl = new CardLabel(cardId, labelId);

        cl.CardId.Should().Be(cardId);
        cl.LabelId.Should().Be(labelId);
    }

    [Fact]
    public void Constructor_EmptyCardId_DoesNotThrow()
    {
        // CardLabel has no validation — just a join entity
        var cl = new CardLabel(Guid.Empty, Guid.NewGuid());
        cl.CardId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void Constructor_EmptyLabelId_DoesNotThrow()
    {
        var cl = new CardLabel(Guid.NewGuid(), Guid.Empty);
        cl.LabelId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void Constructor_BothEmpty_DoesNotThrow()
    {
        var cl = new CardLabel(Guid.Empty, Guid.Empty);
        cl.CardId.Should().Be(Guid.Empty);
        cl.LabelId.Should().Be(Guid.Empty);
    }
}
