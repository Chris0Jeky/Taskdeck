using System.Text;
using FluentAssertions;
using Moq;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Services;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Runs REAL extractors (PdfPig, PlainText) through the budget-wrapped
/// <see cref="ArtefactExtractionService"/> path added for #1369. The budget unit
/// tests use stubs by design; these tests prove result marshaling and
/// stream-position behavior through the Task.Run + WaitAsync path against the
/// real parser libraries, with the default 30s budget never firing for normal
/// documents.
/// </summary>
public sealed class ArtefactExtractionServiceRealExtractorTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _artefactId = Guid.NewGuid();
    private readonly Mock<ISourceArtefactRepository> _artefacts = new();
    private readonly Mock<IArtefactExtractionRepository> _extractions = new();

    [Fact]
    public async Task ExtractAsync_ShouldRunRealPdfPigExtractorUnderDefaultBudget()
    {
        const string marker = "Real PdfPig parse through the budgeted service path.";
        var pdf = BuildPdf(marker);
        ArrangeStoredArtefact("application/pdf", pdf);
        ArtefactExtraction? stored = null;
        _extractions
            .Setup(repository => repository.TryAddForUserAsync(
                It.IsAny<ArtefactExtraction>(),
                _userId,
                It.IsAny<CancellationToken>()))
            .Callback<ArtefactExtraction, Guid, CancellationToken>((value, _, _) => stored = value)
            .ReturnsAsync(ArtefactExtractionStoreResult.Stored);
        var service = new ArtefactExtractionService(
            _artefacts.Object,
            _extractions.Object,
            [new PdfPigArtefactTextExtractor()],
            new ArtefactStorageSettings());

        var result = await service.ExtractAsync(_userId, _artefactId);

        result.IsSuccess.Should().BeTrue();
        result.Value.ExtractedText.Should().Contain(marker);
        result.Value.Warnings.Should().BeEmpty();
        result.Value.ExtractorName.Should().Be("PdfPig");
        stored.Should().NotBeNull();
        stored!.ExtractedText.Should().Contain(marker);
        stored.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractAsync_ShouldRunRealPlainTextExtractorUnderDefaultBudget()
    {
        const string content = "Real plain-text extraction through the budgeted service path.";
        ArrangeStoredArtefact("text/plain", Encoding.UTF8.GetBytes(content));
        ArtefactExtraction? stored = null;
        _extractions
            .Setup(repository => repository.TryAddForUserAsync(
                It.IsAny<ArtefactExtraction>(),
                _userId,
                It.IsAny<CancellationToken>()))
            .Callback<ArtefactExtraction, Guid, CancellationToken>((value, _, _) => stored = value)
            .ReturnsAsync(ArtefactExtractionStoreResult.Stored);
        var service = new ArtefactExtractionService(
            _artefacts.Object,
            _extractions.Object,
            [new PlainTextArtefactTextExtractor()],
            new ArtefactStorageSettings());

        var result = await service.ExtractAsync(_userId, _artefactId);

        result.IsSuccess.Should().BeTrue();
        result.Value.ExtractedText.Should().Be(content);
        result.Value.Warnings.Should().BeEmpty();
        result.Value.ExtractorName.Should().Be("PlainText");
        stored!.ExtractedText.Should().Be(content);
    }

    [Fact]
    public async Task ExtractAsync_ShouldRecordDecodedSizeLimitWarningRowForFlateBomb()
    {
        // A decompression bomb driven through the FULL service path: the real PdfPig
        // extractor bounded to a 64 KiB ceiling must record one warning-bearing
        // history row (decoded-size-limit, empty text), never a crash or extractor
        // error, and the request must return promptly.
        var bomb = FlateBombPdf.Build(inflatedBytes: 8 * 1024 * 1024);
        ArrangeStoredArtefact("application/pdf", bomb);
        ArtefactExtraction? stored = null;
        var storeCalls = 0;
        _extractions
            .Setup(repository => repository.TryAddForUserAsync(
                It.IsAny<ArtefactExtraction>(),
                _userId,
                It.IsAny<CancellationToken>()))
            .Callback<ArtefactExtraction, Guid, CancellationToken>((value, _, _) =>
            {
                stored = value;
                storeCalls++;
            })
            .ReturnsAsync(ArtefactExtractionStoreResult.Stored);
        var service = new ArtefactExtractionService(
            _artefacts.Object,
            _extractions.Object,
            [new PdfPigArtefactTextExtractor(new ArtefactStorageSettings { ExtractionMaxDecodedBytes = 64 * 1024 })],
            new ArtefactStorageSettings());

        var start = System.Diagnostics.Stopwatch.StartNew();
        var result = await service.ExtractAsync(_userId, _artefactId);
        start.Stop();

        result.IsSuccess.Should().BeTrue();
        result.Value.ExtractedText.Should().BeEmpty();
        result.Value.Warnings.Should().Equal(ArtefactExtractionWarningCodes.DecodedSizeLimit);
        stored.Should().NotBeNull();
        stored!.Warnings.Should().Equal(ArtefactExtractionWarningCodes.DecodedSizeLimit);
        stored.ExtractedText.Should().BeEmpty();
        storeCalls.Should().Be(1);
        start.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10),
            "the bomb is aborted at the decoded ceiling, not fully inflated");
    }

    private void ArrangeStoredArtefact(string mimeType, byte[] content)
    {
        var kind = mimeType == "application/pdf" ? ArtefactKind.Pdf : ArtefactKind.TextFile;
        var artefact = new SourceArtefact(
            _userId,
            kind,
            mimeType,
            kind == ArtefactKind.Pdf ? "source.pdf" : "source.txt",
            content.LongLength,
            new string('a', 64),
            CaptureSource.Import);
        typeof(Taskdeck.Domain.Common.Entity)
            .GetProperty(nameof(Taskdeck.Domain.Common.Entity.Id))!
            .SetValue(artefact, _artefactId);

        _artefacts
            .Setup(repository => repository.GetByIdForUserAsync(
                _artefactId,
                _userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(artefact);
        _artefacts
            .Setup(repository => repository.CopyContentForUserAsync(
                _artefactId,
                _userId,
                It.IsAny<Stream>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (Guid _, Guid _, Stream destination, CancellationToken cancellationToken) =>
            {
                await destination.WriteAsync(content, cancellationToken);
                return true;
            });
    }

    private static byte[] BuildPdf(string pageText)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.A4);
        page.AddText(pageText, 12, new PdfPoint(40, 780), font);
        return builder.Build();
    }
}
