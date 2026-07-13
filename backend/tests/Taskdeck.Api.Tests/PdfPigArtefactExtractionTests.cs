using FluentAssertions;
using Taskdeck.Application.Services;
using Taskdeck.Infrastructure.Services;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace Taskdeck.Api.Tests;

public sealed class PdfPigArtefactExtractionTests
{
    private readonly PdfPigArtefactTextExtractor _extractor = new();

    [Fact]
    public void CanExtract_ShouldMatchOnlyPdfMimeType()
    {
        _extractor.CanExtract("application/pdf").Should().BeTrue();
        _extractor.CanExtract("APPLICATION/PDF; version=1.7").Should().BeTrue();
        _extractor.CanExtract("text/plain").Should().BeFalse();
    }

    [Fact]
    public async Task ExtractAsync_ShouldReadRealPdfTextLayer()
    {
        var pdf = BuildPdf(["Taskdeck evidence survives local extraction."]);
        await using var stream = new MemoryStream(pdf);

        var result = await _extractor.ExtractAsync(stream);

        result.ExtractedText.Should().Contain("Taskdeck evidence survives local extraction.");
        result.Warnings.Should().BeEmpty();
        result.ExtractorName.Should().Be("PdfPig");
        result.ExtractorVersion.Should().Be("0.1.15");
    }

    [Fact]
    public async Task ExtractAsync_ShouldReportNoTextLayerForImageOnlyPdf()
    {
        var pdf = BuildImageOnlyPdf();
        await using var stream = new MemoryStream(pdf);

        var result = await _extractor.ExtractAsync(stream);

        result.ExtractedText.Should().BeEmpty();
        result.Warnings.Should().Equal(ArtefactExtractionWarningCodes.NoTextLayer);
    }

    [Fact]
    public async Task ExtractAsync_ShouldStopAtPageCap()
    {
        var pages = Enumerable.Range(1, PdfPigArtefactTextExtractor.MaxPages + 1)
            .Select(page => $"Page marker {page}")
            .Cast<string?>()
            .ToArray();
        var pdf = BuildPdf(pages);
        await using var stream = new MemoryStream(pdf);

        var result = await _extractor.ExtractAsync(stream);

        result.Warnings.Should().Contain(ArtefactExtractionWarningCodes.PageLimit);
        result.ExtractedText.Should().Contain("Page marker 100");
        result.ExtractedText.Should().NotContain("Page marker 101");
    }

    [Fact]
    public async Task ExtractAsync_ShouldStopAtCharacterCap()
    {
        var pages = Enumerable.Range(1, 60)
            .Select(page => $"page-{page}:" + new string('x', 1000))
            .Cast<string?>()
            .ToArray();
        var pdf = BuildPdf(pages);
        await using var stream = new MemoryStream(pdf);

        var result = await _extractor.ExtractAsync(stream);

        result.ExtractedText.Should().HaveLength(PdfPigArtefactTextExtractor.MaxExtractedCharacters);
        result.Warnings.Should().Contain(ArtefactExtractionWarningCodes.CharacterLimit);
    }

    [Fact]
    public async Task ExtractAsync_ShouldRejectBytesAboveSourceArtefactCapBeforeParsing()
    {
        await using var stream = new MemoryStream(
            new byte[(int)PdfPigArtefactTextExtractor.MaxInputBytes + 1]);

        var result = await _extractor.ExtractAsync(stream);

        result.ExtractedText.Should().BeEmpty();
        result.Warnings.Should().Equal(ArtefactExtractionWarningCodes.InputTooLarge);
    }

    private static byte[] BuildPdf(IReadOnlyList<string?> pageTexts)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        foreach (var text in pageTexts)
        {
            var page = builder.AddPage(PageSize.A4);
            if (text is not null)
                page.AddText(text, 12, new PdfPoint(40, 780), font);
        }

        return builder.Build();
    }

    private static byte[] BuildImageOnlyPdf()
    {
        const string onePixelPng =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        page.AddPng(
            Convert.FromBase64String(onePixelPng),
            new PdfRectangle(40, 700, 140, 800));
        return builder.Build();
    }
}
