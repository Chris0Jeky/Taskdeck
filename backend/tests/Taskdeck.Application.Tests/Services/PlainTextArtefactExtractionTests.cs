using System.Text;
using FluentAssertions;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public sealed class PlainTextArtefactExtractionTests
{
    private readonly PlainTextArtefactTextExtractor _extractor = new();

    [Theory]
    [InlineData("text/plain")]
    [InlineData("TEXT/MARKDOWN; charset=utf-8")]
    public void CanExtract_ShouldMatchSupportedTextMimeTypes(string mimeType)
    {
        _extractor.CanExtract(mimeType).Should().BeTrue();
        _extractor.CanExtract("application/pdf").Should().BeFalse();
    }

    [Fact]
    public async Task ExtractAsync_ShouldPassThroughValidMarkdown()
    {
        const string markdown = "# Plan\r\n\r\n- keep evidence\r\n";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(markdown));

        var result = await _extractor.ExtractAsync(stream);

        result.ExtractedText.Should().Be(markdown);
        result.Warnings.Should().BeEmpty();
        result.ExtractorName.Should().Be("PlainText");
        result.ExtractorVersion.Should().Be("1.0");
    }

    [Fact]
    public async Task ExtractAsync_ShouldReturnHonestWarningForOversizedText()
    {
        var bytes = Encoding.UTF8.GetBytes(new string('x', PlainTextArtefactTextExtractor.MaxInputCharacters + 1));
        await using var stream = new MemoryStream(bytes);

        var result = await _extractor.ExtractAsync(stream);

        result.ExtractedText.Should().BeEmpty();
        result.Warnings.Should().Equal(ArtefactExtractionWarningCodes.InputTooLarge);
    }

    [Fact]
    public async Task ExtractAsync_ShouldReturnHonestWarningForInvalidUtf8()
    {
        await using var stream = new MemoryStream([0xC3, 0x28]);

        var result = await _extractor.ExtractAsync(stream);

        result.ExtractedText.Should().BeEmpty();
        result.Warnings.Should().Equal(ArtefactExtractionWarningCodes.InvalidUtf8);
    }

    [Fact]
    public async Task ExtractAsync_ShouldReturnHonestWarningForBinaryControlCharacters()
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("safe\0unsafe"));

        var result = await _extractor.ExtractAsync(stream);

        result.ExtractedText.Should().BeEmpty();
        result.Warnings.Should().Equal(ArtefactExtractionWarningCodes.InvalidText);
    }
}
