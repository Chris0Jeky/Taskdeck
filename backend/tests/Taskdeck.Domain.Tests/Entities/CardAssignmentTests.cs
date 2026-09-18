using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class CardAssignmentTests
{
    [Fact]
    public void SetReplacementPreservesRetainedMetadataAndNoOpVersion()
    {
        var card = new Card(Guid.NewGuid(), Guid.NewGuid(), "Responsibility");
        var actor = Guid.NewGuid(); var a = Guid.NewGuid(); var b = Guid.NewGuid();
        card.Assignments.Should().BeEmpty();
        card.ReplaceAssignments([a, a], actor).Should().BeTrue();
        var retained = card.Assignments.Single(); var stamp = card.UpdatedAt;
        card.ReplaceAssignments([a], Guid.NewGuid()).Should().BeFalse();
        card.UpdatedAt.Should().Be(stamp);
        card.ReplaceAssignments([b, a], actor).Should().BeTrue();
        card.Assignments.Should().Contain(retained);
        retained.AssignedByUserId.Should().Be(actor);
        card.ReplaceAssignments([], actor).Should().BeTrue();
        card.Assignments.Should().BeEmpty();
    }

    [Fact]
    public void ArchivedCardsRejectReplacementButAllowEligibilityCleanup()
    {
        var card = new Card(Guid.NewGuid(), Guid.NewGuid(), "Archived responsibility");
        var user = Guid.NewGuid();
        card.ReplaceAssignments([user], user); card.Archive();
        var replace = () => card.ReplaceAssignments([], user);
        replace.Should().Throw<DomainException>();
        card.DetachAssignment(user).Should().BeTrue();
        card.Assignments.Should().BeEmpty();
        card.IsArchived.Should().BeTrue();
    }
}
