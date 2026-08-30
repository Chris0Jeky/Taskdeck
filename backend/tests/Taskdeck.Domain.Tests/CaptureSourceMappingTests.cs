using FluentAssertions;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Domain.Tests;

public sealed class CaptureSourceMappingTests
{
    [Fact]
    public void Resolve_ShouldCoverEveryLegacyValue()
    {
        foreach (var source in Enum.GetValues<CaptureSource>())
        {
            var act = () => CaptureSourceMapping.Resolve(source);

            act.Should().NotThrow($"every CaptureSource value needs a dimension row ({source})");
            Enum.IsDefined(CaptureSourceMapping.Resolve(source).Modality).Should().BeTrue();
        }
    }

    [Fact]
    public void Resolve_ShouldRejectUndefinedValues()
    {
        var act = () => CaptureSourceMapping.Resolve((CaptureSource)999);

        act.Should().Throw<Taskdeck.Domain.Exceptions.DomainException>().WithMessage("*no dimension mapping*");
    }

    [Theory]
    [InlineData(CaptureSource.Typed)]
    [InlineData(CaptureSource.Import)]
    [InlineData(CaptureSource.Voice)]
    [InlineData(CaptureSource.MeetingIntegration)]
    [InlineData(CaptureSource.ShareTarget)]
    [InlineData(CaptureSource.BrowserExtension)]
    [InlineData(CaptureSource.VsCodeExtension)]
    public void RoundTrip_ShouldBeLosslessForUnambiguousSources(CaptureSource source)
    {
        var dimensions = CaptureSourceMapping.Resolve(source);

        var back = CaptureSourceMapping.ToLegacySource(dimensions.Modality, dimensions.Origin, dimensions.Producer);

        back.Should().Be(source);
        CaptureSourceMapping.IsAmbiguousLegacySource(source).Should().BeFalse();
    }

    [Fact]
    public void RoundTrip_TranscriptFile_NeedsTheTranscriptHint()
    {
        var dimensions = CaptureSourceMapping.Resolve(CaptureSource.TranscriptFile);

        CaptureSourceMapping.ToLegacySource(dimensions.Modality, dimensions.Origin, dimensions.Producer, transcriptHint: true)
            .Should().Be(CaptureSource.TranscriptFile);
        CaptureSourceMapping.ToLegacySource(dimensions.Modality, dimensions.Origin, dimensions.Producer)
            .Should().Be(CaptureSource.TranscriptFile, "a document uploaded as a file is the legacy transcript-file source");
    }

    [Theory]
    [InlineData(CaptureSource.Paste, CaptureSource.Typed)]
    [InlineData(CaptureSource.TranscriptPaste, CaptureSource.Typed)]
    [InlineData(CaptureSource.WebClip, CaptureSource.BrowserExtension)]
    [InlineData(CaptureSource.MarkdownImport, CaptureSource.Import)]
    public void RoundTrip_ShouldCollapseAmbiguousSourcesToTheirSibling(CaptureSource source, CaptureSource expectedSibling)
    {
        var dimensions = CaptureSourceMapping.Resolve(source);

        var back = CaptureSourceMapping.ToLegacySource(dimensions.Modality, dimensions.Origin, dimensions.Producer);

        back.Should().Be(expectedSibling);
        CaptureSourceMapping.IsAmbiguousLegacySource(source).Should().BeTrue();
    }

    [Fact]
    public void ToLegacySource_ShouldPreferTranscriptPasteWhenHinted()
    {
        CaptureSourceMapping
            .ToLegacySource(CaptureModality.Text, CaptureOriginAdapter.WebComposer, CaptureProducerKind.Human, transcriptHint: true)
            .Should().Be(CaptureSource.TranscriptPaste);
    }

    [Fact]
    public void ToLegacySource_ShouldMapAudioToVoiceRegardlessOfOrigin()
    {
        CaptureSourceMapping
            .ToLegacySource(CaptureModality.Audio, CaptureOriginAdapter.Mcp, CaptureProducerKind.Agent)
            .Should().Be(CaptureSource.Voice);
    }

    [Fact]
    public void ToLegacySource_ShouldMapNewOriginsToTyped()
    {
        CaptureSourceMapping
            .ToLegacySource(CaptureModality.Text, CaptureOriginAdapter.Api, CaptureProducerKind.Human)
            .Should().Be(CaptureSource.Typed);
        CaptureSourceMapping
            .ToLegacySource(CaptureModality.Text, CaptureOriginAdapter.Mcp, CaptureProducerKind.Agent)
            .Should().Be(CaptureSource.Typed);
    }
}
