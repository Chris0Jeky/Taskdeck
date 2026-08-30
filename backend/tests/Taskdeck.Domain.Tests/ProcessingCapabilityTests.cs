using FluentAssertions;
using Taskdeck.Domain.Processing;
using Xunit;

namespace Taskdeck.Domain.Tests;

public sealed class ProcessingCapabilityTests
{
    [Fact]
    public void All_ShouldBeDistinctKnownAndDotted()
    {
        ProcessingCapability.All.Should().HaveCount(13);
        ProcessingCapability.All.Should().OnlyHaveUniqueItems();

        foreach (var capability in ProcessingCapability.All)
        {
            ProcessingCapability.IsKnown(capability).Should().BeTrue();
            capability.Should().MatchRegex("^[a-z]+\\.[a-z-]+$");
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("audio.Transcribe")]
    [InlineData("board.mutate")]
    public void IsKnown_ShouldRejectUnknownOrMiscasedValues(string? capability)
    {
        ProcessingCapability.IsKnown(capability).Should().BeFalse();
        ProcessingCapability.DomainOf(capability).Should().BeNull();
    }

    [Fact]
    public void DomainOf_ShouldReturnThePrefix()
    {
        ProcessingCapability.DomainOf(ProcessingCapability.AudioTranscribe).Should().Be("audio");
        ProcessingCapability.DomainOf(ProcessingCapability.DocumentExtractText).Should().Be("document");
    }

    [Fact]
    public void RepresentationProducing_ShouldExcludeUnderstandingAndChangeCapabilities()
    {
        ProcessingCapability.RepresentationProducing.Should().BeSubsetOf(ProcessingCapability.All);
        ProcessingCapability.RepresentationProducing.Should().NotContain(ProcessingCapability.SemanticExtract);
        ProcessingCapability.RepresentationProducing.Should().NotContain(ProcessingCapability.ContextResolve);
        ProcessingCapability.RepresentationProducing.Should().NotContain(ProcessingCapability.ChangePlan);
        ProcessingCapability.RepresentationProducing.Should().NotContain(ProcessingCapability.ChangeVerify);
    }
}
