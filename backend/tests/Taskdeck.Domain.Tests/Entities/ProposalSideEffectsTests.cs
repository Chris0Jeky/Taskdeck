using FluentAssertions;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class ProposalSideEffectsTests
{
    private static Reversibility DefaultReversibility()
        => new("Low risk · confirm before apply", "Confirm affected items.", Reversibility.DefaultWindowMs);

    private static List<SideEffectRow> DefaultRows()
        => new()
        {
            new SideEffectRow("Cards", "Creates cards", SideEffectTone.Active),
            new SideEffectRow("Subtasks", "Not supported", SideEffectTone.Passive),
            new SideEffectRow("Comments", "No comments", SideEffectTone.Passive),
            new SideEffectRow("Activity log", "Audit entries recorded", SideEffectTone.Active),
            new SideEffectRow("Notifications", "Generates notifications", SideEffectTone.Active),
            new SideEffectRow("Webhooks", "No webhooks configured", SideEffectTone.Passive),
            new SideEffectRow("Calendar", "Not yet integrated", SideEffectTone.Passive),
        };

    [Fact]
    public void Constructor_ShouldCreateSideEffects_WithValidData()
    {
        var rows = DefaultRows();
        var rev = DefaultReversibility();

        var sideEffects = new ProposalSideEffects(rows, rev);

        sideEffects.Rows.Should().HaveCount(7);
        sideEffects.Reversibility.Should().Be(rev);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenRowsIsNull()
    {
        var rev = DefaultReversibility();

        var act = () => new ProposalSideEffects(null!, rev);

        act.Should().Throw<ArgumentException>()
            .WithMessage("Side-effect rows cannot be null or empty.*");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenRowsIsEmpty()
    {
        var rev = DefaultReversibility();

        var act = () => new ProposalSideEffects(new List<SideEffectRow>(), rev);

        act.Should().Throw<ArgumentException>()
            .WithMessage("Side-effect rows cannot be null or empty.*");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenReversibilityIsNull()
    {
        var rows = DefaultRows();

        var act = () => new ProposalSideEffects(rows, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Equals_SameValues_ShouldBeEqual()
    {
        var sideEffects1 = new ProposalSideEffects(DefaultRows(), DefaultReversibility());
        var sideEffects2 = new ProposalSideEffects(DefaultRows(), DefaultReversibility());

        sideEffects1.Should().Be(sideEffects2);
        sideEffects1.Equals(sideEffects2).Should().BeTrue();
        (sideEffects1.GetHashCode() == sideEffects2.GetHashCode()).Should().BeTrue();
    }

    [Fact]
    public void Equals_DifferentRows_ShouldNotBeEqual()
    {
        var rows1 = DefaultRows();
        var rows2 = new List<SideEffectRow>
        {
            new SideEffectRow("Cards", "Different value", SideEffectTone.Passive),
            new SideEffectRow("Subtasks", "Not supported", SideEffectTone.Passive),
            new SideEffectRow("Comments", "No comments", SideEffectTone.Passive),
            new SideEffectRow("Activity log", "Audit entries recorded", SideEffectTone.Active),
            new SideEffectRow("Notifications", "Generates notifications", SideEffectTone.Active),
            new SideEffectRow("Webhooks", "No webhooks configured", SideEffectTone.Passive),
            new SideEffectRow("Calendar", "Not yet integrated", SideEffectTone.Passive),
        };

        var sideEffects1 = new ProposalSideEffects(rows1, DefaultReversibility());
        var sideEffects2 = new ProposalSideEffects(rows2, DefaultReversibility());

        sideEffects1.Should().NotBe(sideEffects2);
    }

    [Fact]
    public void Equals_DifferentReversibility_ShouldNotBeEqual()
    {
        var rev1 = DefaultReversibility();
        var rev2 = new Reversibility("3 hours", "Different", 10_800_000L);

        var sideEffects1 = new ProposalSideEffects(DefaultRows(), rev1);
        var sideEffects2 = new ProposalSideEffects(DefaultRows(), rev2);

        sideEffects1.Should().NotBe(sideEffects2);
    }

    [Fact]
    public void Equals_Null_ShouldNotBeEqual()
    {
        var sideEffects = new ProposalSideEffects(DefaultRows(), DefaultReversibility());

        sideEffects.Equals(null).Should().BeFalse();
    }

    [Fact]
    public void Equals_DifferentRowCount_ShouldNotBeEqual()
    {
        var rows1 = DefaultRows();
        var rows2 = new List<SideEffectRow>
        {
            new SideEffectRow("Cards", "Creates cards", SideEffectTone.Active)
        };

        var sideEffects1 = new ProposalSideEffects(rows1, DefaultReversibility());
        var sideEffects2 = new ProposalSideEffects(rows2, DefaultReversibility());

        sideEffects1.Should().NotBe(sideEffects2);
    }
}
