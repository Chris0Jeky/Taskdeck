using System.Text;
using System.Text.Json;
using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class LlmCaptureTriageExtractorTests
{
    private readonly Mock<ILlmProvider> _providerMock;
    private readonly Mock<ILlmKillSwitchService> _killSwitchMock;
    private readonly Mock<ILlmQuotaService> _quotaMock;
    private readonly LlmCaptureTriageSettings _settings;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _boardId = Guid.NewGuid();
    private readonly Guid _reservationId = Guid.NewGuid();

    public LlmCaptureTriageExtractorTests()
    {
        _providerMock = new Mock<ILlmProvider>();
        _killSwitchMock = new Mock<ILlmKillSwitchService>();
        _quotaMock = new Mock<ILlmQuotaService>();
        _settings = new LlmCaptureTriageSettings();

        // Live, healthy, non-mock provider by default; individual tests override.
        _providerMock
            .Setup(p => p.GetHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmHealthStatus(true, "OpenAI", Model: "gpt-4o-mini"));
        _killSwitchMock
            .Setup(k => k.IsKilledAsync(It.IsAny<LlmSurface?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        // Atomic quota reservation succeeds by default (issue #1313); individual tests override.
        _quotaMock
            .Setup(q => q.ReserveAsync(
                It.IsAny<Guid>(),
                It.IsAny<LlmSurface>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QuotaReservationDto(true, null, _reservationId, 100_000, 60));
    }

    private LlmCaptureTriageExtractor BuildExtractor(ILlmCaptureTriageProgressReporter? progressReporter = null)
        => new(
            _providerMock.Object,
            _settings,
            _killSwitchMock.Object,
            _quotaMock.Object,
            progressReporter: progressReporter);

    private static CapturePayloadV1 TranscriptPayload(string text = "Alice: I'll send the report by Friday.")
        => new(CaptureRequestContract.CurrentSchemaVersion, CaptureSource.TranscriptPaste, text);

    private void SetupCompletion(string content, int tokensUsed = 250, bool isDegraded = false, string? degradedReason = null)
    {
        _providerMock
            .Setup(p => p.CompleteAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmCompletionResult(
                content,
                tokensUsed,
                IsActionable: false,
                Provider: "OpenAI",
                Model: "gpt-4o-mini",
                IsDegraded: isDegraded,
                DegradedReason: degradedReason));
    }

    private void SetupCompletionForRequest(
        Func<ChatCompletionRequest, string> contentFactory,
        int tokensUsed = 250)
    {
        _providerMock
            .Setup(p => p.CompleteAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatCompletionRequest request, CancellationToken _) => new LlmCompletionResult(
                contentFactory(request),
                tokensUsed,
                IsActionable: false,
                Provider: "OpenAI",
                Model: "gpt-4o-mini"));
    }

    private static string ExactQuoteFromRequest(ChatCompletionRequest request)
    {
        var content = request.Messages.Single().Content;
        return content.First(character => !char.IsWhiteSpace(character)).ToString();
    }

    private static string V2Completion(params (string Title, string EvidenceQuote)[] tasks)
    {
        return JsonSerializer.Serialize(new
        {
            tasks = tasks.Select(task => new
            {
                title = task.Title,
                type = "action",
                assigneeHint = (string?)null,
                dueDateHint = (string?)null,
                confidence = 0.9m,
                evidenceQuote = task.EvidenceQuote
            }).ToArray()
        });
    }

    [Fact]
    public async Task ExtractAsync_ShouldReturnStrictV2ContractOutput_WhenCompletionIsValidJson()
    {
        SetupCompletion("""{"tasks":[{"title":"Send the report","type":"action","assigneeHint":"Alice","dueDateHint":null,"confidence":0.95,"evidenceQuote":"Alice: I'll send the report by Friday."}]}""");
        var extractor = BuildExtractor();

        var result = await extractor.ExtractAsync(_userId, _boardId, TranscriptPayload());

        result.Outcome.Should().Be(LlmCaptureTriageOutcome.Succeeded);
        result.Succeeded.Should().BeTrue();
        result.Provider.Should().Be("OpenAI");
        result.Model.Should().Be("gpt-4o-mini");
        result.Output.Should().NotBeNull();
        result.Output!.PromptVersion.Should().Be(CaptureTriageOutputContract.PromptVersionLlmV2);
        result.Output.Version.Should().Be(CaptureTriageOutputContract.SchemaVersionV2);
        result.Output.Tasks.Should().ContainSingle()
            .Which.Title.Should().Be("Send the report");
        result.Output.Tasks[0].Type.Should().Be("action");
        result.Output.Tasks[0].AssigneeHint.Should().Be("Alice");
        result.Output.Tasks[0].Confidence.Should().Be(0.95m);
        result.Output.Tasks[0].EvidenceQuote.Should().Be("Alice: I'll send the report by Friday.");
    }

    [Fact]
    public async Task ExtractAsync_UsesUtf16OffsetsForUniqueEvidenceIncludingEmoji()
    {
        const string transcript = "😀 Alice: send the report.";
        const string quote = "Alice: send the report.";
        SetupCompletion($$"""{"tasks":[{"title":"Send report","type":"action","assigneeHint":null,"dueDateHint":null,"confidence":0.9,"evidenceQuote":"{{quote}}"}]}""");
        var result = await BuildExtractor().ExtractAsync(
            _userId,
            _boardId,
            TranscriptPayload(transcript));

        result.Succeeded.Should().BeTrue();
        result.EvidenceSpans.Should().ContainSingle();
        result.EvidenceSpans![0].Should().Be((3, 3 + quote.Length));
    }

    [Fact]
    public async Task ExtractAsync_DoesNotLinkOverlappingRepeatedQuote()
    {
        const string transcript = "aaa";
        SetupCompletion("""{"tasks":[{"title":"Inspect text","type":"action","assigneeHint":null,"dueDateHint":null,"confidence":0.9,"evidenceQuote":"aa"}]}""");
        var result = await BuildExtractor().ExtractAsync(
            _userId,
            _boardId,
            TranscriptPayload(transcript));

        result.Succeeded.Should().BeTrue();
        result.EvidenceSpans.Should().ContainSingle().Which.Should().BeNull();
    }

    [Fact]
    public async Task ExtractAsync_DuplicateTitleWithDifferentRangesHasNoStructuredSpan()
    {
        const string transcript = "Alpha quote.\nBeta quote.";
        SetupCompletion(V2Completion(
            ("Review item", "Alpha quote."),
            ("review item", "Beta quote.")));
        var result = await BuildExtractor().ExtractAsync(
            _userId,
            _boardId,
            TranscriptPayload(transcript));

        result.Succeeded.Should().BeTrue();
        result.Output!.Tasks.Should().ContainSingle();
        result.EvidenceSpans.Should().ContainSingle().Which.Should().BeNull();
    }

    [Fact]
    public async Task ExtractAsync_ShouldTolerateCodeFences_WhenJsonUsesTheExactV2Shape()
    {
        SetupCompletion("""
            Here is the extraction you asked for:
            ```json
            {"tasks":[
              {"title":"Send the report","type":"action","assigneeHint":null,"dueDateHint":null,"confidence":0.9,"evidenceQuote":"I'll send the report by Friday."},
              {"title":"Book the venue","type":"action","assigneeHint":"Bob","dueDateHint":null,"confidence":0.8,"evidenceQuote":"Bob: I can book the venue."}
            ]}
            ```
            """);
        var extractor = BuildExtractor();

        var result = await extractor.ExtractAsync(
            _userId,
            _boardId,
            TranscriptPayload("Alice: I'll send the report by Friday.\nBob: I can book the venue."));

        result.Outcome.Should().Be(LlmCaptureTriageOutcome.Succeeded);
        result.Output!.Tasks.Should().HaveCount(2);
        result.Output.Tasks[1].Title.Should().Be("Book the venue");
    }

    [Fact]
    public async Task ExtractAsync_ShouldRejectOverlongFields_InsteadOfChangingModelOutput()
    {
        var longTitle = new string('t', 400);
        var longEvidence = new string('e', 600);
        SetupCompletion($$"""{"tasks":[{"title":"{{longTitle}}","type":"action","assigneeHint":null,"dueDateHint":null,"confidence":0.9,"evidenceQuote":"{{longEvidence}}"}]}""");
        var extractor = BuildExtractor();

        var result = await extractor.ExtractAsync(_userId, _boardId, TranscriptPayload());

        result.Outcome.Should().Be(LlmCaptureTriageOutcome.InvalidOutput);
        result.Output.Should().BeNull();
    }

    [Fact]
    public async Task ExtractAsync_ShouldRejectMoreThanTheV2TaskCap_InsteadOfTruncating()
    {
        var tasks = string.Join(",", Enumerable.Range(0, 30).Select(i =>
            $$"""{"title":"Task {{i % 25}}","type":"action","assigneeHint":null,"dueDateHint":null,"confidence":0.9,"evidenceQuote":"Alice:"}"""));
        SetupCompletion($$"""{"tasks":[{{tasks}}]}""");
        var extractor = BuildExtractor();

        var result = await extractor.ExtractAsync(_userId, _boardId, TranscriptPayload());

        result.Outcome.Should().Be(LlmCaptureTriageOutcome.InvalidOutput);
        result.Output.Should().BeNull();
    }

    [Fact]
    public async Task ExtractAsync_ShouldDedupeOverlapTitlesWithoutChangingTheFirstV2Task()
    {
        SetupCompletion("""{"tasks":[{"title":"Send the report","type":"action","assigneeHint":"Alice","dueDateHint":null,"confidence":0.9,"evidenceQuote":"Alice: I'll send the report by Friday."},{"title":"send the report","type":"decision","assigneeHint":null,"dueDateHint":null,"confidence":0.4,"evidenceQuote":"I'll send the report by Friday."}]}""");
        var extractor = BuildExtractor();

        var result = await extractor.ExtractAsync(_userId, _boardId, TranscriptPayload());

        result.Outcome.Should().Be(LlmCaptureTriageOutcome.Succeeded);
        result.Output!.Tasks.Should().ContainSingle();
        result.Output.Tasks[0].Type.Should().Be("action");
        result.Output.Tasks[0].AssigneeHint.Should().Be("Alice");
        result.Output.Tasks[0].Confidence.Should().Be(0.9m);
    }

    [Fact]
    public async Task ExtractAsync_ShouldMapReduceLongTranscriptAndDedupeAcrossChunks()
    {
        _settings.MaxInputTokensPerChunk = 64;
        _settings.ChunkOverlapTokens = 16;
        SetupCompletionForRequest(request => V2Completion(("Send the launch notes", ExactQuoteFromRequest(request))));
        var transcript = string.Join("\n\n", Enumerable.Repeat(
            "Alice: I will send the launch notes after this meeting.",
            8));
        var expectedChunkCount = TranscriptTriageChunker.Chunk(
            transcript,
            _settings.MaxInputTokensPerChunk,
            _settings.ChunkOverlapTokens).Count;
        var extractor = BuildExtractor();

        var result = await extractor.ExtractAsync(_userId, _boardId, TranscriptPayload(transcript));

        expectedChunkCount.Should().BeGreaterThan(1);
        result.Outcome.Should().Be(LlmCaptureTriageOutcome.Succeeded);
        result.Output!.Tasks.Should().ContainSingle();
        result.Output.Tasks[0].Title.Should().Be("Send the launch notes");
        _providerMock.Verify(
            provider => provider.CompleteAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()),
            Times.Exactly(expectedChunkCount));
    }

    [Fact]
    public async Task ExtractAsync_ShouldNotLinkAQuoteRepeatedAcrossNonOverlappingMapChunks()
    {
        _settings.MaxInputTokensPerChunk = 24;
        _settings.ChunkOverlapTokens = 0;
        const string quote = "same evidence";
        var transcript = $"Alice: {quote}\n\nBob: {quote}";
        var chunks = TranscriptTriageChunker.Chunk(
            transcript,
            _settings.MaxInputTokensPerChunk,
            _settings.ChunkOverlapTokens);
        chunks.Should().HaveCount(2);
        chunks.Should().OnlyContain(chunk => chunk.Text.Contains(quote, StringComparison.Ordinal));
        SetupCompletionForRequest(request => request.Messages.Single().Content == chunks[0].Text
            ? V2Completion(("Use evidence", quote))
            : "{\"tasks\":[]}");
        var extractor = BuildExtractor();

        var result = await extractor.ExtractAsync(_userId, _boardId, TranscriptPayload(transcript));

        result.Succeeded.Should().BeTrue();
        result.Output!.Tasks.Should().ContainSingle().Which.Title.Should().Be("Use evidence");
        result.EvidenceSpans.Should().ContainSingle().Which.Should().BeNull();
    }

    [Fact]
    public async Task ExtractAsync_ShouldReportProgressBeforeAndAfterEachMapCompletion()
    {
        _settings.MaxInputTokensPerChunk = 64;
        _settings.ChunkOverlapTokens = 16;
        SetupCompletionForRequest(request => V2Completion(("Send the launch notes", ExactQuoteFromRequest(request))));
        var transcript = string.Join("\n\n", Enumerable.Repeat(
            "Alice: I will send the launch notes after this meeting.",
            8));
        var expectedChunkCount = TranscriptTriageChunker.Chunk(
            transcript,
            _settings.MaxInputTokensPerChunk,
            _settings.ChunkOverlapTokens).Count;
        var progressReporter = new Mock<ILlmCaptureTriageProgressReporter>();
        var extractor = BuildExtractor(progressReporter.Object);

        var result = await extractor.ExtractAsync(_userId, _boardId, TranscriptPayload(transcript));

        result.Succeeded.Should().BeTrue();
        progressReporter.Verify(
            reporter => reporter.ReportProgress(),
            Times.Exactly(expectedChunkCount * 2));
    }

    [Fact]
    public async Task ExtractAsync_ShouldReportProgressAfterProviderFailure()
    {
        _providerMock
            .Setup(provider => provider.CompleteAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("provider failed"));
        var progressReporter = new Mock<ILlmCaptureTriageProgressReporter>();
        var extractor = BuildExtractor(progressReporter.Object);

        var act = () => extractor.ExtractAsync(_userId, _boardId, TranscriptPayload());

        await act.Should().ThrowAsync<InvalidOperationException>();
        progressReporter.Verify(reporter => reporter.ReportProgress(), Times.Exactly(2));
    }

    [Fact]
    public async Task ExtractAsync_ShouldReserveEachMapChunkWithItsUtf8ByteRequestBound()
    {
        _settings.MaxInputTokensPerChunk = 64;
        _settings.ChunkOverlapTokens = 16;
        _settings.MaxOutputTokens = 512;
        SetupCompletion("""{"tasks":[{"title":"Send the launch notes","type":"action","assigneeHint":null,"dueDateHint":null,"confidence":0.9,"evidenceQuote":"a"}]}""");
        var transcript = string.Join(' ', Enumerable.Repeat("a", 256));
        var chunks = TranscriptTriageChunker.Chunk(
            transcript,
            _settings.MaxInputTokensPerChunk,
            _settings.ChunkOverlapTokens);
        var reservationEstimates = new List<int>();
        _quotaMock
            .Setup(quota => quota.ReserveAsync(
                _userId,
                LlmSurface.CaptureTriage,
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, LlmSurface, int, CancellationToken>((_, _, estimate, _) => reservationEstimates.Add(estimate))
            .ReturnsAsync(new QuotaReservationDto(true, null, _reservationId, 100_000, 60));
        var extractor = BuildExtractor();

        var result = await extractor.ExtractAsync(_userId, _boardId, TranscriptPayload(transcript));

        result.Outcome.Should().Be(LlmCaptureTriageOutcome.Succeeded);
        reservationEstimates.Should().Equal(chunks.Select(chunk =>
            Encoding.UTF8.GetByteCount(LlmCaptureTriagePrompt.SystemPrompt) +
            Encoding.UTF8.GetByteCount(chunk.Text) +
            _settings.MaxOutputTokens));
    }

    [Fact]
    public async Task ExtractAsync_ShouldDiscardMappedOutput_WhenALaterChunkCannotReserveQuota()
    {
        _settings.MaxInputTokensPerChunk = 64;
        _settings.ChunkOverlapTokens = 16;
        SetupCompletionForRequest(request => V2Completion(("Discard me", ExactQuoteFromRequest(request))));
        _quotaMock
            .SetupSequence(quota => quota.ReserveAsync(
                _userId,
                LlmSurface.CaptureTriage,
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QuotaReservationDto(true, null, _reservationId, 100_000, 60))
            .ReturnsAsync(new QuotaReservationDto(false, "Daily token budget exhausted", null, 0, 0));
        var transcript = string.Join("\n\n", Enumerable.Repeat(
            "Alice: This transcript has enough content to require another bounded map chunk.",
            8));
        var extractor = BuildExtractor();

        var result = await extractor.ExtractAsync(_userId, _boardId, TranscriptPayload(transcript));

        result.Outcome.Should().Be(LlmCaptureTriageOutcome.QuotaExceeded);
        result.Output.Should().BeNull("a later map-leg quota denial must not emit partial output");
        _providerMock.Verify(
            provider => provider.CompleteAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExtractAsync_ShouldIncludeLaterChunkTask_WhenEarlyChunkAlreadyUsesTheV2TaskCap()
    {
        _settings.MaxInputTokensPerChunk = 64;
        _settings.ChunkOverlapTokens = 16;
        var call = 0;
        _providerMock
            .Setup(provider => provider.CompleteAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatCompletionRequest request, CancellationToken _) => new LlmCompletionResult(
                call++ == 0
                    ? V2Completion(Enumerable.Range(0, CaptureTriageOutputContract.MaxTasks)
                        .Select(index => ($"Early task {index}", ExactQuoteFromRequest(request)))
                        .ToArray())
                    : V2Completion(("Later follow-up", ExactQuoteFromRequest(request))),
                100,
                IsActionable: false,
                Provider: "OpenAI",
                Model: "gpt-4o-mini"));
        var transcript = string.Join("\n\n", Enumerable.Repeat(
            "Alice: This transcript has enough content to require more than one map chunk.",
            8));
        var extractor = BuildExtractor();

        var result = await extractor.ExtractAsync(_userId, _boardId, TranscriptPayload(transcript));

        result.Outcome.Should().Be(LlmCaptureTriageOutcome.Succeeded);
        result.Output!.Tasks.Should().HaveCount(CaptureTriageOutputContract.MaxTasks);
        result.Output.Tasks.Select(task => task.Title).Should().Contain("Later follow-up",
            "the deterministic reduce must retain coverage outside the first map chunk");
    }

    [Fact]
    public async Task ExtractAsync_ShouldPreserveNoActionVerdict_WhenEveryChunkIsEmpty()
    {
        _settings.MaxInputTokensPerChunk = 64;
        _settings.ChunkOverlapTokens = 16;
        SetupCompletion("""{"tasks":[]}""");
        var transcript = string.Join("\n\n", Enumerable.Repeat(
            "Alice: We discussed the weather and exchanged greetings.",
            8));
        var expectedChunkCount = TranscriptTriageChunker.Chunk(
            transcript,
            _settings.MaxInputTokensPerChunk,
            _settings.ChunkOverlapTokens).Count;
        var extractor = BuildExtractor();

        var result = await extractor.ExtractAsync(_userId, _boardId, TranscriptPayload(transcript));

        expectedChunkCount.Should().BeGreaterThan(1);
        result.Outcome.Should().Be(LlmCaptureTriageOutcome.EmptyExtraction);
        result.Provider.Should().Be("OpenAI");
        result.Model.Should().Be("gpt-4o-mini");
        result.Output.Should().BeNull();
        _providerMock.Verify(
            provider => provider.CompleteAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()),
            Times.Exactly(expectedChunkCount));
    }

    [Fact]
    public async Task ExtractAsync_ShouldDiscardMappedTasks_WhenALaterChunkDegrades()
    {
        _settings.MaxInputTokensPerChunk = 64;
        _settings.ChunkOverlapTokens = 16;
        var call = 0;
        _providerMock
            .Setup(provider => provider.CompleteAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatCompletionRequest request, CancellationToken _) =>
            {
                if (call++ == 0)
                {
                    return new LlmCompletionResult(
                        V2Completion(("Discard me", ExactQuoteFromRequest(request))),
                        100,
                        IsActionable: false,
                        Provider: "OpenAI",
                        Model: "gpt-4o-mini");
                }

                return new LlmCompletionResult(
                    string.Empty,
                    100,
                    IsActionable: false,
                    Provider: "OpenAI",
                    Model: "gpt-4o-mini",
                    IsDegraded: true,
                    DegradedReason: "provider timeout");
            });
        var transcript = string.Join("\n\n", Enumerable.Repeat(
            "Alice: This long transcript has more content than one map chunk.",
            8));
        var extractor = BuildExtractor();

        var result = await extractor.ExtractAsync(_userId, _boardId, TranscriptPayload(transcript));

        result.Outcome.Should().Be(LlmCaptureTriageOutcome.ProviderDegraded);
        result.Output.Should().BeNull("partial map output must never become a proposal candidate");
        result.Provider.Should().BeNull("discarded mapped output must not be stamped as LLM provenance");
        _providerMock.Verify(
            provider => provider.CompleteAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task ExtractAsync_ShouldDeclineBeforeQuotaReservation_WhenMapChunkCallBudgetWouldBeExceeded()
    {
        _settings.MaxInputTokensPerChunk = 64;
        _settings.ChunkOverlapTokens = 0;
        _settings.MaxChunkCount = 1;
        var transcript = string.Join("\n\n", Enumerable.Repeat(
            "Alice: This transcript requires more than one bounded map chunk.",
            8));
        var extractor = BuildExtractor();

        var result = await extractor.ExtractAsync(_userId, _boardId, TranscriptPayload(transcript));

        result.Outcome.Should().Be(LlmCaptureTriageOutcome.InvalidOutput);
        result.Detail.Should().Contain("map-chunk call budget");
        _providerMock.Verify(
            provider => provider.CompleteAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _quotaMock.Verify(
            quota => quota.ReserveAsync(
                It.IsAny<Guid>(),
                It.IsAny<LlmSurface>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExtractAsync_ShouldReturnDisabled_WithoutTouchingProviderOrGuardrails()
    {
        _settings.Enabled = false;
        var extractor = BuildExtractor();

        var result = await extractor.ExtractAsync(_userId, _boardId, TranscriptPayload());

        result.Outcome.Should().Be(LlmCaptureTriageOutcome.Disabled);
        _providerMock.Verify(p => p.CompleteAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _killSwitchMock.Verify(k => k.IsKilledAsync(It.IsAny<LlmSurface?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExtractAsync_ShouldReturnKillSwitchActive_WhenCaptureTriageSurfaceIsKilled()
    {
        _killSwitchMock
            .Setup(k => k.IsKilledAsync(LlmSurface.CaptureTriage, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var extractor = BuildExtractor();

        var result = await extractor.ExtractAsync(_userId, _boardId, TranscriptPayload());

        result.Outcome.Should().Be(LlmCaptureTriageOutcome.KillSwitchActive);
        _providerMock.Verify(p => p.CompleteAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExtractAsync_ShouldReturnProviderIsMock_WhenNoLiveProviderResolves()
    {
        _providerMock
            .Setup(p => p.GetHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmHealthStatus(true, "Mock", IsMock: true));
        var extractor = BuildExtractor();

        var result = await extractor.ExtractAsync(_userId, _boardId, TranscriptPayload());

        // "A live provider is configured" is the REVIVAL-08 selection condition: the mock's canned
        // chat output can never satisfy the triage contract, so the call is skipped entirely.
        result.Outcome.Should().Be(LlmCaptureTriageOutcome.ProviderIsMock);
        _providerMock.Verify(p => p.CompleteAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExtractAsync_ShouldReturnProviderUnavailable_WhenHealthCheckFails()
    {
        _providerMock
            .Setup(p => p.GetHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmHealthStatus(false, "OpenAI", "API key is missing"));
        var extractor = BuildExtractor();

        var result = await extractor.ExtractAsync(_userId, _boardId, TranscriptPayload());

        result.Outcome.Should().Be(LlmCaptureTriageOutcome.ProviderUnavailable);
        result.Detail.Should().Be("API key is missing");
        _providerMock.Verify(p => p.CompleteAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExtractAsync_ShouldReturnQuotaExceeded_WithoutCallingProvider()
    {
        _quotaMock
            .Setup(q => q.ReserveAsync(
                _userId,
                LlmSurface.CaptureTriage,
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QuotaReservationDto(false, "Daily token budget exhausted", null, 0, 0));
        var extractor = BuildExtractor();

        var result = await extractor.ExtractAsync(_userId, _boardId, TranscriptPayload());

        result.Outcome.Should().Be(LlmCaptureTriageOutcome.QuotaExceeded);
        result.Detail.Should().Be("Daily token budget exhausted");
        _providerMock.Verify(p => p.CompleteAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExtractAsync_ShouldReturnProviderDegraded_AndStillRecordUsage_WhenTokensWereBurned()
    {
        // Truncation is the clearest case: degraded AND billed — usage must be recorded anyway.
        SetupCompletion("{\"tasks\":[{\"title\":\"Trunc", tokensUsed: 4096, isDegraded: true, degradedReason: "Response was truncated");
        var extractor = BuildExtractor();

        var result = await extractor.ExtractAsync(_userId, _boardId, TranscriptPayload());

        result.Outcome.Should().Be(LlmCaptureTriageOutcome.ProviderDegraded);
        result.Detail.Should().Be("Response was truncated");
        result.Provider.Should().BeNull("no output was produced, so no provider may be recorded (#1273)");
        // Tokens were burned → the reservation is committed with the actuals (issue #1313).
        _quotaMock.Verify(
            q => q.CommitReservationAsync(_reservationId, _userId, LlmSurface.CaptureTriage, "OpenAI", "gpt-4o-mini", 4096, 0, It.IsAny<CancellationToken>()),
            Times.Once);
        _quotaMock.Verify(
            q => q.ReleaseReservationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExtractAsync_UnknownCompatibleUsage_CommitsReservationEstimateForLargeTranscript()
    {
        _quotaMock
            .Setup(q => q.ReserveAsync(
                _userId,
                LlmSurface.CaptureTriage,
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QuotaReservationDto(
                true, null, _reservationId, 100_000, 60, EstimatedTokens: 4000));
        _providerMock
            .Setup(p => p.CompleteAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmCompletionResult(
                """{"tasks":[{"title":"Send report","type":"action","assigneeHint":null,"dueDateHint":null,"confidence":0.9,"evidenceQuote":"send report"}]}""",
                0,
                IsActionable: false,
                Provider: "OpenAICompatible",
                Model: "vendor/model")
            {
                HasAuthoritativeTokenUsage = false
            });
        var extractor = BuildExtractor();

        var result = await extractor.ExtractAsync(
            _userId,
            _boardId,
            TranscriptPayload(new string('x', 4000) + " send report"));

        result.Outcome.Should().Be(LlmCaptureTriageOutcome.Succeeded);
        _quotaMock.Verify(q => q.CommitReservationAsync(
            _reservationId,
            _userId,
            LlmSurface.CaptureTriage,
            "OpenAICompatible",
            "vendor/model",
            4000,
            0,
            CancellationToken.None), Times.Once);
        _quotaMock.Verify(q => q.ReleaseReservationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExtractAsync_ShouldReleaseReservation_WhenNoTokensWereConsumed()
    {
        SetupCompletion("not json at all", tokensUsed: 0);
        var extractor = BuildExtractor();

        var result = await extractor.ExtractAsync(_userId, _boardId, TranscriptPayload());

        result.Outcome.Should().Be(LlmCaptureTriageOutcome.InvalidOutput);
        // Zero tokens → no committed usage; the reservation is released so it consumes no quota (#1313).
        _quotaMock.Verify(
            q => q.CommitReservationAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<LlmSurface>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _quotaMock.Verify(
            q => q.ReleaseReservationAsync(_reservationId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExtractAsync_ShouldReleaseReservation_WhenProviderThrows()
    {
        _providerMock
            .Setup(p => p.CompleteAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("provider boom"));
        var extractor = BuildExtractor();

        await FluentActions
            .Awaiting(() => extractor.ExtractAsync(_userId, _boardId, TranscriptPayload()))
            .Should().ThrowAsync<InvalidOperationException>();

        // The reservation must not leak when the provider call fails (issue #1313).
        _quotaMock.Verify(
            q => q.ReleaseReservationAsync(_reservationId, It.IsAny<CancellationToken>()),
            Times.Once);
        _quotaMock.Verify(
            q => q.CommitReservationAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<LlmSurface>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExtractAsync_CancelledAfterDispatch_CommitsReservationEstimate()
    {
        _quotaMock
            .Setup(q => q.ReserveAsync(
                _userId,
                LlmSurface.CaptureTriage,
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QuotaReservationDto(
                true, null, _reservationId, 100_000, 60, EstimatedTokens: 2000));
        _providerMock
            .Setup(p => p.CompleteAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()))
            .Returns((ChatCompletionRequest request, CancellationToken _) =>
            {
                request.DispatchContext.Observe("OpenAICompatible", "vendor/model");
                request.DispatchContext.MarkDispatched();
                return Task.FromException<LlmCompletionResult>(new OperationCanceledException());
            });
        var extractor = BuildExtractor();

        await FluentActions
            .Awaiting(() => extractor.ExtractAsync(_userId, _boardId, TranscriptPayload()))
            .Should().ThrowAsync<OperationCanceledException>();

        _quotaMock.Verify(q => q.CommitReservationAsync(
            _reservationId,
            _userId,
            LlmSurface.CaptureTriage,
            "OpenAICompatible",
            "vendor/model",
            2000,
            0,
            CancellationToken.None), Times.Once);
        _quotaMock.Verify(q => q.ReleaseReservationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExtractAsync_ObservedPreDispatchResult_ReleasesReservation()
    {
        _providerMock
            .Setup(p => p.CompleteAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatCompletionRequest request, CancellationToken _) =>
            {
                request.DispatchContext.Observe("OpenAICompatible", "vendor/model");
                return new LlmCompletionResult(
                    "configuration rejected",
                    0,
                    IsActionable: false,
                    Provider: "OpenAICompatible",
                    Model: "vendor/model",
                    IsDegraded: true)
                {
                    HasAuthoritativeTokenUsage = false,
                    ShouldSettleQuotaReservation = true
                };
            });
        var extractor = BuildExtractor();

        var result = await extractor.ExtractAsync(_userId, _boardId, TranscriptPayload());

        result.Outcome.Should().Be(LlmCaptureTriageOutcome.ProviderDegraded);
        _quotaMock.Verify(q => q.ReleaseReservationAsync(_reservationId, CancellationToken.None), Times.Once);
        _quotaMock.Verify(q => q.CommitReservationAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<LlmSurface>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExtractAsync_ShouldReturnInvalidOutput_WhenContentIsUnparseable()
    {
        SetupCompletion("I could not find any structured tasks, sorry!");
        var extractor = BuildExtractor();

        var result = await extractor.ExtractAsync(_userId, _boardId, TranscriptPayload());

        result.Outcome.Should().Be(LlmCaptureTriageOutcome.InvalidOutput);
        result.Output.Should().BeNull();
    }

    [Fact]
    public async Task ExtractAsync_ShouldReturnEmptyExtractionWithProviderIdentity_WhenModelDeliberatelyReportsNoTasks()
    {
        SetupCompletion("""{"tasks":[]}""");
        var extractor = BuildExtractor();

        var result = await extractor.ExtractAsync(_userId, _boardId, TranscriptPayload());

        result.Outcome.Should().Be(LlmCaptureTriageOutcome.EmptyExtraction);
        // The LLM genuinely ran and produced this verdict — its identity is reported so the
        // "triaged, nothing to propose" outcome carries honest provenance (#1273).
        result.Provider.Should().Be("OpenAI");
        result.Model.Should().Be("gpt-4o-mini");
        result.Output.Should().BeNull();
    }

    [Fact]
    public async Task ExtractAsync_ShouldRejectTheWholeCompletion_WhenAnyV2TaskIsMalformed()
    {
        SetupCompletion("""{"tasks":[{"title":"Send the report","type":"action","assigneeHint":null,"dueDateHint":null,"confidence":0.9,"evidenceQuote":"Alice: I'll send the report by Friday."},{"title":"Malformed casing","type":"Action","assigneeHint":null,"dueDateHint":null,"confidence":0.9,"evidenceQuote":"Alice: I'll send the report by Friday."}]}""");
        var extractor = BuildExtractor();

        var result = await extractor.ExtractAsync(_userId, _boardId, TranscriptPayload());

        // Entries were returned but none survived — malformed output (fallback), not an empty verdict.
        result.Outcome.Should().Be(LlmCaptureTriageOutcome.InvalidOutput);
        result.Output.Should().BeNull();
    }

    [Theory]
    [InlineData("alice: I'll send the report by Friday.")]
    [InlineData("Alice:  I'll send the report by Friday.")]
    [InlineData("Alice: I'll send the report by Friday. ")]
    public async Task ExtractAsync_ShouldRejectEvidenceQuote_WhenItIsNotAnExactOrdinalSubstring(string evidenceQuote)
    {
        SetupCompletion($$"""{"tasks":[{"title":"Send the report","type":"action","assigneeHint":null,"dueDateHint":null,"confidence":0.9,"evidenceQuote":"{{evidenceQuote}}"}]}""");
        var extractor = BuildExtractor();

        var result = await extractor.ExtractAsync(_userId, _boardId, TranscriptPayload());

        result.Outcome.Should().Be(LlmCaptureTriageOutcome.InvalidOutput);
        result.Output.Should().BeNull();
    }

    [Fact]
    public async Task ExtractAsync_ShouldRequireExactCrLfAndUnicodeEvidenceQuote()
    {
        const string transcript = "Åsa: Принято — ship it.\r\nBob: Done.";
        SetupCompletion("""{"tasks":[{"title":"Ship it","type":"decision","assigneeHint":"Åsa","dueDateHint":null,"confidence":1,"evidenceQuote":"Åsa: Принято — ship it."}]}""");
        var extractor = BuildExtractor();

        var exactResult = await extractor.ExtractAsync(_userId, _boardId, TranscriptPayload(transcript));

        exactResult.Outcome.Should().Be(LlmCaptureTriageOutcome.Succeeded);
        exactResult.Output!.Tasks[0].EvidenceQuote.Should().Be("Åsa: Принято — ship it.");

        SetupCompletion("""{"tasks":[{"title":"Ship it","type":"decision","assigneeHint":"Åsa","dueDateHint":null,"confidence":1,"evidenceQuote":"ship it.\nBob"}]}""");

        var lineEndingChangedResult = await extractor.ExtractAsync(_userId, _boardId, TranscriptPayload(transcript));

        lineEndingChangedResult.Outcome.Should().Be(LlmCaptureTriageOutcome.InvalidOutput);
        lineEndingChangedResult.Output.Should().BeNull();
    }

    [Fact]
    public async Task ExtractAsync_ShouldRejectAnEvidenceQuoteThatOnlyExistsAcrossMapChunks()
    {
        _settings.MaxInputTokensPerChunk = 8;
        _settings.ChunkOverlapTokens = 0;
        var transcript = string.Join(' ', Enumerable.Range(1, 20).Select(index => $"word{index}"));
        var crossChunkQuote = string.Join(' ', Enumerable.Range(1, 12).Select(index => $"word{index}"));
        SetupCompletion($$"""{"tasks":[{"title":"Cross chunk task","type":"action","assigneeHint":null,"dueDateHint":null,"confidence":0.9,"evidenceQuote":"{{crossChunkQuote}}"}]}""");
        var extractor = BuildExtractor();

        var result = await extractor.ExtractAsync(_userId, _boardId, TranscriptPayload(transcript));

        result.Outcome.Should().Be(LlmCaptureTriageOutcome.InvalidOutput);
        result.Output.Should().BeNull();
        _providerMock.Verify(
            provider => provider.CompleteAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()),
            Times.Once(),
            "the first map leg must reject a quote that only spans multiple chunks");
    }

    [Fact]
    public async Task ExtractAsync_ShouldSendTriagePromptWithAttributionAndSettings()
    {
        ChatCompletionRequest? sent = null;
        _providerMock
            .Setup(p => p.CompleteAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ChatCompletionRequest, CancellationToken>((req, _) => sent = req)
            .ReturnsAsync(new LlmCompletionResult(
                """{"tasks":[{"title":"T","type":"action","assigneeHint":null,"dueDateHint":null,"confidence":0.9,"evidenceQuote":"the transcript body"}]}""",
                100,
                IsActionable: false,
                Provider: "OpenAI",
                Model: "gpt-4o-mini"));
        _settings.MaxOutputTokens = 1234;
        _settings.Temperature = 0.5;
        var extractor = BuildExtractor();

        await extractor.ExtractAsync(_userId, _boardId, TranscriptPayload("the transcript body"));

        sent.Should().NotBeNull();
        // A non-null SystemPrompt opts out of the providers' chat instruction-extraction mode.
        sent!.SystemPrompt.Should().Be(LlmCaptureTriagePrompt.SystemPrompt);
        sent.MaxTokens.Should().Be(1234);
        sent.Temperature.Should().Be(0.5);
        sent.Messages.Should().ContainSingle().Which.Content.Should().Be("the transcript body");
        sent.Attribution.Should().NotBeNull();
        sent.Attribution!.UserId.Should().Be(_userId);
        sent.Attribution.BoardId.Should().Be(_boardId);
        sent.Attribution.SourceSurface.Should().Be(LlmRequestSourceSurface.Capture);
    }

    [Fact]
    public async Task ExtractAsync_ShouldWorkWithoutOptionalGuardrailServices()
    {
        SetupCompletion("""{"tasks":[{"title":"T","type":"action","assigneeHint":null,"dueDateHint":null,"confidence":0.9,"evidenceQuote":"Alice: I'll send the report by Friday."}]}""");
        var extractor = new LlmCaptureTriageExtractor(_providerMock.Object, _settings);

        var result = await extractor.ExtractAsync(_userId, _boardId, TranscriptPayload());

        result.Outcome.Should().Be(LlmCaptureTriageOutcome.Succeeded);
    }
}
