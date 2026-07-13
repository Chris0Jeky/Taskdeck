using System.Text;
using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public sealed class ArtefactExtractionServiceTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _artefactId = Guid.NewGuid();
    private readonly Mock<ISourceArtefactRepository> _artefacts = new();
    private readonly Mock<IArtefactExtractionRepository> _extractions = new();

    [Fact]
    public async Task ExtractAsync_ShouldUseFirstMimeMatchAndNormalizeLineEndingsOnce()
    {
        ArrangeStoredArtefact("text/markdown", Encoding.UTF8.GetBytes("ignored"));
        ArtefactExtraction? stored = null;
        _extractions
            .Setup(repository => repository.TryAddForUserAsync(
                It.IsAny<ArtefactExtraction>(),
                _userId,
                It.IsAny<CancellationToken>()))
            .Callback<ArtefactExtraction, Guid, CancellationToken>((value, _, _) => stored = value)
            .ReturnsAsync(ArtefactExtractionStoreResult.Stored);

        var first = new StubExtractor(
            "text/",
            new ArtefactExtractionResult("first\r\nsecond\rthird", [], "First", "1.0"),
            "First");
        var second = new StubExtractor(
            "text/",
            new ArtefactExtractionResult("wrong", [], "Second", "1.0"),
            "Second");
        var service = CreateService(first, second);

        var result = await service.ExtractAsync(_userId, _artefactId);

        result.IsSuccess.Should().BeTrue();
        result.Value.ExtractedText.Should().Be("first\nsecond\nthird");
        result.Value.TextLength.Should().Be(18);
        stored.Should().NotBeNull();
        stored!.ExtractedText.Should().Be("first\nsecond\nthird");
        first.CallCount.Should().Be(1);
        second.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ExtractAsync_ShouldPersistContentFreeWarningWhenExtractorThrows()
    {
        ArrangeStoredArtefact("application/pdf", [1, 2, 3]);
        ArtefactExtraction? stored = null;
        _extractions
            .Setup(repository => repository.TryAddForUserAsync(
                It.IsAny<ArtefactExtraction>(),
                _userId,
                It.IsAny<CancellationToken>()))
            .Callback<ArtefactExtraction, Guid, CancellationToken>((value, _, _) => stored = value)
            .ReturnsAsync(ArtefactExtractionStoreResult.Stored);
        var extractor = new StubExtractor("application/pdf", exception: new InvalidDataException("sensitive parser detail"));
        var service = CreateService(extractor);

        var result = await service.ExtractAsync(_userId, _artefactId);

        result.IsSuccess.Should().BeTrue();
        result.Value.ExtractedText.Should().BeEmpty();
        result.Value.Warnings.Should().Equal(ArtefactExtractionWarningCodes.ExtractorError);
        stored!.WarningsJson.Should().NotContain("sensitive parser detail");
    }

    [Fact]
    public async Task ExtractAsync_ShouldDefensivelyCapExtractorOutputWithoutSplittingEmoji()
    {
        ArrangeStoredArtefact("text/plain", Encoding.UTF8.GetBytes("ignored"));
        _extractions
            .Setup(repository => repository.TryAddForUserAsync(
                It.IsAny<ArtefactExtraction>(),
                _userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ArtefactExtractionStoreResult.Stored);
        var oversized = new string('x', ArtefactExtraction.MaxExtractedTextLength - 1) + "\U0001F600tail";
        var extractor = new StubExtractor(
            "text/plain",
            new ArtefactExtractionResult(oversized, [], "First", "1.0"),
            "First");
        var service = CreateService(extractor);

        var result = await service.ExtractAsync(_userId, _artefactId);

        result.IsSuccess.Should().BeTrue();
        result.Value.ExtractedText.Should().HaveLength(ArtefactExtraction.MaxExtractedTextLength - 1);
        char.IsHighSurrogate(result.Value.ExtractedText[^1]).Should().BeFalse();
        result.Value.Warnings.Should().Contain(ArtefactExtractionWarningCodes.CharacterLimit);
    }

    [Fact]
    public async Task ExtractAsync_ShouldNotCreateRecordForUnsupportedMimeType()
    {
        ArrangeStoredArtefact("image/png", [1, 2, 3]);
        var service = CreateService(new StubExtractor("text/"));

        var result = await service.ExtractAsync(_userId, _artefactId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        _extractions.Verify(repository => repository.TryAddForUserAsync(
            It.IsAny<ArtefactExtraction>(),
            It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExtractAsync_ShouldPersistWarningWithoutReadingDeclaredOversizedContent()
    {
        ArrangeStoredArtefact(
            "application/pdf",
            [1, 2, 3],
            declaredByteSize: 5);
        ArtefactExtraction? stored = null;
        _extractions
            .Setup(repository => repository.TryAddForUserAsync(
                It.IsAny<ArtefactExtraction>(),
                _userId,
                It.IsAny<CancellationToken>()))
            .Callback<ArtefactExtraction, Guid, CancellationToken>((value, _, _) => stored = value)
            .ReturnsAsync(ArtefactExtractionStoreResult.Stored);
        var service = CreateService(new StubExtractor("application/pdf", inputByteLimit: 4));

        var result = await service.ExtractAsync(_userId, _artefactId);

        result.IsSuccess.Should().BeTrue();
        result.Value.ExtractedText.Should().BeEmpty();
        result.Value.Warnings.Should().Equal(ArtefactExtractionWarningCodes.InputTooLarge);
        stored!.Warnings.Should().Equal(ArtefactExtractionWarningCodes.InputTooLarge);
        _artefacts.Verify(repository => repository.CopyContentForUserAsync(
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            It.IsAny<Stream>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExtractAsync_ShouldStopCopyWhenStoredBytesExceedDeclaredSize()
    {
        ArrangeStoredArtefact(
            "application/pdf",
            [1, 2, 3, 4, 5],
            declaredByteSize: 3);
        _extractions
            .Setup(repository => repository.TryAddForUserAsync(
                It.IsAny<ArtefactExtraction>(),
                _userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ArtefactExtractionStoreResult.Stored);
        var service = CreateService(new StubExtractor("application/pdf", inputByteLimit: 4));

        var result = await service.ExtractAsync(_userId, _artefactId);

        result.IsSuccess.Should().BeTrue();
        result.Value.ExtractedText.Should().BeEmpty();
        result.Value.Warnings.Should().Equal(ArtefactExtractionWarningCodes.InputTooLarge);
    }

    [Fact]
    public async Task ExtractAsync_ShouldFailWhenSourceDisappearsBeforeCommit()
    {
        ArrangeStoredArtefact("text/plain", Encoding.UTF8.GetBytes("content"));
        _extractions
            .Setup(repository => repository.TryAddForUserAsync(
                It.IsAny<ArtefactExtraction>(),
                _userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ArtefactExtractionStoreResult.SourceArtefactUnavailable);
        var service = CreateService(new StubExtractor("text/plain"));

        var result = await service.ExtractAsync(_userId, _artefactId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task GetLatestAsync_ShouldReturnRepositoryWinner()
    {
        var extraction = new ArtefactExtraction(
            _artefactId,
            "PlainText",
            "1.0",
            [],
            "latest");
        _extractions
            .Setup(repository => repository.GetLatestForArtefactForUserAsync(
                _artefactId,
                _userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(extraction);
        var service = CreateService();

        var result = await service.GetLatestAsync(_userId, _artefactId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(extraction.Id);
        result.Value.ExtractedText.Should().Be("latest");
    }

    private ArtefactExtractionService CreateService(params IArtefactTextExtractor[] extractors)
        => new(_artefacts.Object, _extractions.Object, extractors);

    private void ArrangeStoredArtefact(
        string mimeType,
        byte[] content,
        long? declaredByteSize = null)
    {
        var kind = mimeType == "application/pdf" ? ArtefactKind.Pdf :
            mimeType.StartsWith("text/", StringComparison.Ordinal) ? ArtefactKind.TextFile :
            ArtefactKind.Image;
        var artefact = new SourceArtefact(
            _userId,
            kind,
            mimeType,
            kind == ArtefactKind.Pdf ? "source.pdf" : kind == ArtefactKind.Image ? "source.png" : "source.md",
            declaredByteSize ?? content.LongLength,
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

    private sealed class StubExtractor : IArtefactTextExtractor
    {
        private readonly string _mimePrefix;
        private readonly ArtefactExtractionResult _result;
        private readonly Exception? _exception;

        public StubExtractor(
            string mimePrefix,
            ArtefactExtractionResult? result = null,
            string name = "Stub",
            Exception? exception = null,
            long inputByteLimit = 1024 * 1024)
        {
            _mimePrefix = mimePrefix;
            ExtractorName = name;
            ExtractorVersion = "1.0";
            _result = result ?? new ArtefactExtractionResult("content", [], name, "1.0");
            _exception = exception;
            InputByteLimit = inputByteLimit;
        }

        public string ExtractorName { get; }
        public string ExtractorVersion { get; }
        public long InputByteLimit { get; }
        public int CallCount { get; private set; }

        public bool CanExtract(string mimeType)
            => mimeType.StartsWith(_mimePrefix, StringComparison.OrdinalIgnoreCase);

        public Task<ArtefactExtractionResult> ExtractAsync(
            Stream content,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (_exception is not null)
                throw _exception;
            return Task.FromResult(_result);
        }
    }
}
