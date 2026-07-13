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
    public async Task ExtractAsync_ShouldRewindSeekableStreamInsteadOfBuffering()
    {
        var pdf = BuildPdf(["Rewound seekable evidence stays readable."]);
        await using var stream = new MemoryStream(pdf);
        // Simulate a caller that already consumed the stream; a seekable stream must
        // be rewound and read directly rather than buffered from its current position.
        stream.Position = stream.Length;

        var result = await _extractor.ExtractAsync(stream);

        result.ExtractedText.Should().Contain("Rewound seekable evidence stays readable.");
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractAsync_ShouldBoundSingleTextHeavyPage()
    {
        var oversizedPage = new string('a', PdfPigArtefactTextExtractor.MaxExtractedCharacters + 5_000);
        var pdf = BuildPdf([oversizedPage]);
        await using var stream = new MemoryStream(pdf);

        var result = await _extractor.ExtractAsync(stream);

        result.ExtractedText.Should().HaveLength(PdfPigArtefactTextExtractor.MaxExtractedCharacters);
        result.Warnings.Should().Contain(ArtefactExtractionWarningCodes.CharacterLimit);
    }

    [Fact]
    public async Task ExtractAsync_ShouldNotClaimNoTextLayerWhenPagesWereSkipped()
    {
        // First MaxPages pages are image-only; a later (skipped) page carries text.
        // The scanned window has no text, but page-limit already signals truncation,
        // so no-text-layer would misdiagnose the document.
        var pdf = BuildImageOnlyPdfWithTrailingText(
            PdfPigArtefactTextExtractor.MaxPages,
            "Selectable text lives on a page past the scan window.");
        await using var stream = new MemoryStream(pdf);

        var result = await _extractor.ExtractAsync(stream);

        result.ExtractedText.Should().BeEmpty();
        result.Warnings.Should().Contain(ArtefactExtractionWarningCodes.PageLimit);
        result.Warnings.Should().NotContain(ArtefactExtractionWarningCodes.NoTextLayer);
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

    private static byte[] BuildImageOnlyPdfWithTrailingText(int imageOnlyPages, string trailingText)
    {
        const string onePixelPng =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";
        var builder = new PdfDocumentBuilder();
        var pngBytes = Convert.FromBase64String(onePixelPng);
        for (var index = 0; index < imageOnlyPages; index++)
        {
            var page = builder.AddPage(PageSize.A4);
            page.AddPng(pngBytes, new PdfRectangle(40, 700, 140, 800));
        }

        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var textPage = builder.AddPage(PageSize.A4);
        textPage.AddText(trailingText, 12, new PdfPoint(40, 780), font);
        return builder.Build();
    }
}
