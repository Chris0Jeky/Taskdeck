using FluentAssertions;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Application.Tests.Representations;

public sealed class RepresentationDescriptorTests
{
    [Fact]
    public void Projection_PreservesHeaderAndProjectsSupersessionWithoutChangingRowQuality()
    {
        var owner = Guid.NewGuid();
        var capture = Guid.NewGuid();
        var parent = Guid.NewGuid();
        Representation Header(RepresentationQualityState quality) => new(
            Guid.NewGuid(), capture, owner, RepresentationKind.Transcript, parent, null, null,
            "legacy.transcript", "1", null, new string('a', 64), 1, new string('b', 64), null,
            quality, ["partial"], DateTimeOffset.UnixEpoch);
        var old = Header(RepresentationQualityState.Provisional);
        var next = Header(RepresentationQualityState.Final);
        var edge = new RepresentationSupersession(old, next);

        var original = RepresentationDescriptor.FromRepresentation(old);
        var superseded = RepresentationDescriptor.FromRepresentation(old, edge);

        original.QualityState.Should().Be(RepresentationQualityState.Provisional);
        original.SupersededByRepresentationId.Should().BeNull();
        superseded.Should().BeEquivalentTo(original, options => options
            .Excluding(item => item.QualityState).Excluding(item => item.SupersededByRepresentationId));
        superseded.QualityState.Should().Be(RepresentationQualityState.Superseded);
        superseded.SupersededByRepresentationId.Should().Be(next.Id);
        old.QualityState.Should().Be(RepresentationQualityState.Provisional);
        Action mismatched = () => RepresentationDescriptor.FromRepresentation(next, edge);
        mismatched.Should().Throw<ArgumentException>();
    }
}
