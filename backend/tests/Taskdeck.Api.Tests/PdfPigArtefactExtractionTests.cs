using FluentAssertions;
using Taskdeck.Api.Tests.Support;
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
    public async Task ExtractAsync_ShouldNotEndInWhitespaceAtCharacterCap()
    {
        // First page fills the budget to exactly one character short; the second page's
        // leading separator then lands on the cap boundary. Without trimming, the
        // extracted text would end in a stray '\n'.
        var firstPage = new string('a', PdfPigArtefactTextExtractor.MaxExtractedCharacters - 1);
        var pdf = BuildPdf([firstPage, "tail"]);
        await using var stream = new MemoryStream(pdf);

        var result = await _extractor.ExtractAsync(stream);

        result.Warnings.Should().Contain(ArtefactExtractionWarningCodes.CharacterLimit);
        result.ExtractedText.Should().NotBeNullOrEmpty();
        result.ExtractedText.Should().Be(
            result.ExtractedText.TrimEnd(),
            "persisted extractions must never end in stray whitespace at the cap");
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

    [Fact]
    public async Task ExtractAsync_ShouldReturnDecodedSizeLimitWarningForFlateBomb()
    {
        // ~8 MiB of decompressed whitespace from a few KiB of FlateDecode input,
        // against a 64 KiB decoded ceiling: the bounded provider must abort during
        // the streaming pre-pass, long before 8 MiB is materialized.
        var bomb = FlateBombPdf.Build(inflatedBytes: 8 * 1024 * 1024);
        bomb.Length.Should().BeLessThan(256 * 1024, "the compressed bomb input is only a few KiB");
        var extractor = new PdfPigArtefactTextExtractor(
            new ArtefactStorageSettings { ExtractionMaxDecodedBytes = 64 * 1024 });
        await using var stream = new MemoryStream(bomb);

        var start = System.Diagnostics.Stopwatch.StartNew();
        var result = await extractor.ExtractAsync(stream);
        start.Stop();

        result.ExtractedText.Should().BeEmpty();
        result.Warnings.Should().Equal(ArtefactExtractionWarningCodes.DecodedSizeLimit);
        result.Warnings.Should().NotContain(ArtefactExtractionWarningCodes.ExtractorError);
        result.Warnings.Should().NotContain(ArtefactExtractionWarningCodes.NoTextLayer);
        start.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5),
            "the pre-pass aborts at the ceiling rather than inflating the whole bomb");
    }

    [Fact]
    public async Task ExtractAsync_ShouldExtractLegitPdfIdenticallyThroughBoundedAndDefaultProviders()
    {
        // Parity control: bounding must not alter the decoded text of a legitimate
        // document. Same PDF, same extraction walk, bounded vs stock provider.
        var pdf = BuildPdf(new string?[]
        {
            "Parity alpha beta gamma line one.",
            "Second page delta epsilon zeta."
        });

        var bounded = new PdfPigArtefactTextExtractor(new ArtefactStorageSettings());
        var unbounded = new PdfPigArtefactTextExtractor(new ArtefactStorageSettings(), boundDecodedOutput: false);

        await using var boundedStream = new MemoryStream(pdf);
        await using var unboundedStream = new MemoryStream(pdf);
        var boundedResult = await bounded.ExtractAsync(boundedStream);
        var unboundedResult = await unbounded.ExtractAsync(unboundedStream);

        boundedResult.ExtractedText.Should().NotBeNullOrEmpty();
        boundedResult.ExtractedText.Should().Be(unboundedResult.ExtractedText);
        boundedResult.Warnings.Should().Equal(unboundedResult.Warnings);
        boundedResult.ExtractedText.Should().Contain("Parity alpha beta gamma line one.");
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
