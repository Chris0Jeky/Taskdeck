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
    public async Task ExtractAsync_ShouldRejectInvalidUtf16FromExtractorAsContractError()
    {
        ArrangeStoredArtefact("text/plain", Encoding.UTF8.GetBytes("ignored"));
        _extractions
            .Setup(repository => repository.TryAddForUserAsync(
                It.IsAny<ArtefactExtraction>(),
                _userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ArtefactExtractionStoreResult.Stored);
        var extractor = new StubExtractor(
            "text/plain",
            new ArtefactExtractionResult("invalid\uD83D", [], "First", "1.0"),
            "First");

        var result = await CreateService(extractor).ExtractAsync(_userId, _artefactId);

        result.IsSuccess.Should().BeTrue();
        result.Value.ExtractedText.Should().BeEmpty();
        result.Value.Warnings.Should().Contain(ArtefactExtractionWarningCodes.ExtractorContractError);
    }

    [Fact]
    public async Task ExtractAsync_ShouldRetainContractWarningWhenExtractorReturnsTooManyWarnings()
    {
        ArrangeStoredArtefact("text/plain", Encoding.UTF8.GetBytes("ignored"));
        _extractions
            .Setup(repository => repository.TryAddForUserAsync(
                It.IsAny<ArtefactExtraction>(),
                _userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ArtefactExtractionStoreResult.Stored);
        var warnings = Enumerable.Range(0, ArtefactExtraction.MaxWarningCount + 1)
            .Select(index => $"warning-{index}")
            .ToArray();
        var extractor = new StubExtractor(
            "text/plain",
            new ArtefactExtractionResult(
                new string('x', ArtefactExtraction.MaxExtractedTextLength + 1),
                warnings,
                "First",
                "1.0"),
            "First");

        var result = await CreateService(extractor).ExtractAsync(_userId, _artefactId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Warnings.Should().HaveCount(ArtefactExtraction.MaxWarningCount);
        result.Value.Warnings.Should().Contain(ArtefactExtractionWarningCodes.ExtractorContractError);
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
    public async Task ExtractAsync_ShouldRecordTimeoutWarningWhenExtractorIgnoresBudget()
    {
        // Models a parser-bomb PDF: the extractor never observes the token (like
        // PdfPig's synchronous Open), so only the wall-clock budget returns control.
        ArrangeStoredArtefact("application/pdf", [1, 2, 3]);
        ArtefactExtraction? stored = null;
        _extractions
            .Setup(repository => repository.TryAddForUserAsync(
                It.IsAny<ArtefactExtraction>(),
                _userId,
                It.IsAny<CancellationToken>()))
            .Callback<ArtefactExtraction, Guid, CancellationToken>((value, _, _) => stored = value)
            .ReturnsAsync(ArtefactExtractionStoreResult.Stored);
        using var release = new ManualResetEventSlim(false);
        var extractor = new StubExtractor("application/pdf", blockUntil: release);
        var settings = new ArtefactStorageSettings { ExtractionTimeoutSeconds = 0.05 };
        var service = CreateService(settings, extractor);

        var result = await service.ExtractAsync(_userId, _artefactId);

        result.IsSuccess.Should().BeTrue();
        result.Value.ExtractedText.Should().BeEmpty();
        result.Value.Warnings.Should().Equal(ArtefactExtractionWarningCodes.ExtractionTimeout);
        stored.Should().NotBeNull();
        stored!.Warnings.Should().Equal(ArtefactExtractionWarningCodes.ExtractionTimeout);
        _extractions.Verify(repository => repository.TryAddForUserAsync(
            It.IsAny<ArtefactExtraction>(),
            _userId,
            It.IsAny<CancellationToken>()), Times.Once);

        release.Set(); // let the abandoned worker unwind
    }

    [Fact]
    public async Task ExtractAsync_ShouldNotTimeOutNormalDocumentWithinBudget()
    {
        ArrangeStoredArtefact("text/markdown", Encoding.UTF8.GetBytes("ignored"));
        _extractions
            .Setup(repository => repository.TryAddForUserAsync(
                It.IsAny<ArtefactExtraction>(),
                _userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ArtefactExtractionStoreResult.Stored);
        var extractor = new StubExtractor(
            "text/",
            new ArtefactExtractionResult("hello world", [], "First", "1.0"),
            "First");
        var settings = new ArtefactStorageSettings { ExtractionTimeoutSeconds = 30 };
        var service = CreateService(settings, extractor);

        var result = await service.ExtractAsync(_userId, _artefactId);

        result.IsSuccess.Should().BeTrue();
        result.Value.ExtractedText.Should().Be("hello world");
        result.Value.Warnings.Should().NotContain(ArtefactExtractionWarningCodes.ExtractionTimeout);
        extractor.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task ExtractAsync_ShouldPropagateCallerCancellationWithoutRecording()
    {
        // Caller cancellation must win over the budget: the request throws and no
        // extraction-history row is written (distinct from the budget-timeout outcome).
        ArrangeStoredArtefact("application/pdf", [1, 2, 3]);
        _extractions
            .Setup(repository => repository.TryAddForUserAsync(
                It.IsAny<ArtefactExtraction>(),
                _userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ArtefactExtractionStoreResult.Stored);
        using var release = new ManualResetEventSlim(false);
        using var started = new ManualResetEventSlim(false);
        var extractor = new StubExtractor(
            "application/pdf",
            blockUntil: release,
            signalStarted: started);
        using var cts = new CancellationTokenSource();
        var settings = new ArtefactStorageSettings { ExtractionTimeoutSeconds = 30 };
        var service = CreateService(settings, extractor);

        var task = service.ExtractAsync(_userId, _artefactId, cts.Token);
        started.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        cts.Cancel();

        await FluentActions
            .Awaiting(() => task)
            .Should()
            .ThrowAsync<OperationCanceledException>();
        _extractions.Verify(repository => repository.TryAddForUserAsync(
            It.IsAny<ArtefactExtraction>(),
            It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never);

        release.Set(); // let the abandoned worker unwind
    }

    [Fact]
    public async Task ExtractAsync_ShouldStopCooperativeExtractorBetweenPagesOnBudget()
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
        var extractor = new CooperativePagedExtractor(
            "application/pdf",
            totalPages: 1000,
            perPageWork: TimeSpan.FromMilliseconds(20));
        var settings = new ArtefactStorageSettings { ExtractionTimeoutSeconds = 0.05 };
        var service = CreateService(settings, extractor);

        var result = await service.ExtractAsync(_userId, _artefactId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Warnings.Should().Equal(ArtefactExtractionWarningCodes.ExtractionTimeout);
        stored!.Warnings.Should().Equal(ArtefactExtractionWarningCodes.ExtractionTimeout);

        // Deterministically observe the worker's final state (the request itself does
        // not wait for it): it stopped between pages via the token, not by finishing.
        (await Task.WhenAny(extractor.Finished, Task.Delay(TimeSpan.FromSeconds(10))))
            .Should().Be(extractor.Finished);
        extractor.CooperativelyCancelled.Should().BeTrue();
        extractor.PagesProcessed.Should().BeLessThan(1000);
    }

    [Fact]
    public async Task ExtractAsync_ShouldRecordExtractorErrorForUnrelatedCancellation()
    {
        // An extractor that throws OperationCanceledException from a token unrelated
        // to the caller's or the budget's is an extractor fault, not a caller
        // cancellation: it must be recorded as a content-free extractor-error row
        // rather than propagated as a spurious cancellation with no history.
        ArrangeStoredArtefact("application/pdf", [1, 2, 3]);
        ArtefactExtraction? stored = null;
        _extractions
            .Setup(repository => repository.TryAddForUserAsync(
                It.IsAny<ArtefactExtraction>(),
                _userId,
                It.IsAny<CancellationToken>()))
            .Callback<ArtefactExtraction, Guid, CancellationToken>((value, _, _) => stored = value)
            .ReturnsAsync(ArtefactExtractionStoreResult.Stored);
        var extractor = new StubExtractor(
            "application/pdf",
            exception: new OperationCanceledException("unrelated token"));
        var settings = new ArtefactStorageSettings { ExtractionTimeoutSeconds = 30 };
        var service = CreateService(settings, extractor);

        var result = await service.ExtractAsync(_userId, _artefactId);

        result.IsSuccess.Should().BeTrue();
        result.Value.ExtractedText.Should().BeEmpty();
        result.Value.Warnings.Should().Equal(ArtefactExtractionWarningCodes.ExtractorError);
        stored!.Warnings.Should().Equal(ArtefactExtractionWarningCodes.ExtractorError);
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

    private ArtefactExtractionService CreateService(
        ArtefactStorageSettings settings,
        params IArtefactTextExtractor[] extractors)
        => new(_artefacts.Object, _extractions.Object, extractors, settings);

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
        private readonly ManualResetEventSlim? _blockUntil;
        private readonly ManualResetEventSlim? _signalStarted;

        public StubExtractor(
            string mimePrefix,
            ArtefactExtractionResult? result = null,
            string name = "Stub",
            Exception? exception = null,
            long inputByteLimit = 1024 * 1024,
            ManualResetEventSlim? blockUntil = null,
            ManualResetEventSlim? signalStarted = null)
        {
            _mimePrefix = mimePrefix;
            ExtractorName = name;
            ExtractorVersion = "1.0";
            _result = result ?? new ArtefactExtractionResult("content", [], name, "1.0");
            _exception = exception;
            InputByteLimit = inputByteLimit;
            _blockUntil = blockUntil;
            _signalStarted = signalStarted;
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

            // Deliberately ignore the cancellation token to model PdfPig's synchronous
            // PdfDocument.Open, which does not honour cancellation; the service must
            // still return by abandoning this worker when the budget fires.
            _signalStarted?.Set();
            _blockUntil?.Wait(TimeSpan.FromSeconds(30));

            return Task.FromResult(_result);
        }
    }

    /// <summary>
    /// Models an extractor that observes cancellation cooperatively between pages,
    /// like PdfPig's page loop. Proves the budget token flows through the service and
    /// stops further page work rather than running to completion.
    /// </summary>
    private sealed class CooperativePagedExtractor : IArtefactTextExtractor
    {
        private readonly string _mimePrefix;
        private readonly int _totalPages;
        private readonly TimeSpan _perPageWork;
        private readonly TaskCompletionSource _finished =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CooperativePagedExtractor(string mimePrefix, int totalPages, TimeSpan perPageWork)
        {
            _mimePrefix = mimePrefix;
            _totalPages = totalPages;
            _perPageWork = perPageWork;
        }

        public string ExtractorName => "CooperativePaged";
        public string ExtractorVersion => "1.0";
        public long InputByteLimit => 1024 * 1024;
        public int PagesProcessed { get; private set; }
        public bool CooperativelyCancelled { get; private set; }

        /// <summary>Completes when the worker unwinds (completion or cancellation).</summary>
        public Task Finished => _finished.Task;

        public bool CanExtract(string mimeType)
            => mimeType.StartsWith(_mimePrefix, StringComparison.OrdinalIgnoreCase);

        public Task<ArtefactExtractionResult> ExtractAsync(
            Stream content,
            CancellationToken cancellationToken = default)
        {
            try
            {
                for (var page = 1; page <= _totalPages; page++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Thread.Sleep(_perPageWork);
                    PagesProcessed++;
                }

                return Task.FromResult(
                    new ArtefactExtractionResult("done", [], ExtractorName, ExtractorVersion));
            }
            catch (OperationCanceledException)
            {
                CooperativelyCancelled = true;
                throw;
            }
            finally
            {
                _finished.TrySetResult();
            }
        }
    }
}
