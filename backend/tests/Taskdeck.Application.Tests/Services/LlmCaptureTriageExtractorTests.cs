using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
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
            .Setup(q => q.ReserveAsync(It.IsAny<Guid>(), It.IsAny<LlmSurface>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QuotaReservationDto(true, null, _reservationId, 100_000, 60));
    }

    private LlmCaptureTriageExtractor BuildExtractor()
        => new(_providerMock.Object, _settings, _killSwitchMock.Object, _quotaMock.Object);

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

    [Fact]
    public async Task ExtractAsync_ShouldReturnSanitizedContractValidOutput_WhenCompletionIsValidJson()
    {
        SetupCompletion("""{"tasks":[{"title":"Send the report","evidence":"Alice: I'll send the report by Friday."}]}""");
        var extractor = BuildExtractor();

        var result = await extractor.ExtractAsync(_userId, _boardId, TranscriptPayload());

        result.Outcome.Should().Be(LlmCaptureTriageOutcome.Succeeded);
        result.Succeeded.Should().BeTrue();
        result.Provider.Should().Be("OpenAI");
        result.Model.Should().Be("gpt-4o-mini");
        result.Output.Should().NotBeNull();
        result.Output!.PromptVersion.Should().Be(CaptureTriageOutputContract.PromptVersionLlmV1);
        result.Output.Version.Should().Be(CaptureTriageOutputContract.SchemaVersion);
        result.Output.Tasks.Should().ContainSingle()
            .Which.Title.Should().Be("Send the report");
    }

    [Fact]
    public async Task ExtractAsync_ShouldTolerateCodeFencesAndExtraJsonFields()
    {
        SetupCompletion("""
            Here is the extraction you asked for:
            ```json
            {"reasoning":"two commitments found","tasks":[
              {"title":"Send the report","evidence":"I'll send the report by Friday.","confidence":0.9},
              {"title":"Book the venue","evidence":"Bob: I can book the venue."}
            ]}
            ```
            """);
        var extractor = BuildExtractor();

        var result = await extractor.ExtractAsync(_userId, _boardId, TranscriptPayload());

        result.Outcome.Should().Be(LlmCaptureTriageOutcome.Succeeded);
        result.Output!.Tasks.Should().HaveCount(2);
        result.Output.Tasks[1].Title.Should().Be("Book the venue");
    }

    [Fact]
    public async Task ExtractAsync_ShouldTruncateOverlongTitlesAndEvidence_InsteadOfRejecting()
    {
        var longTitle = new string('t', 400);
        var longEvidence = new string('e', 600);
        SetupCompletion($$"""{"tasks":[{"title":"{{longTitle}}","evidence":"{{longEvidence}}"}]}""");
        var extractor = BuildExtractor();

        var result = await extractor.ExtractAsync(_userId, _boardId, TranscriptPayload());

        result.Outcome.Should().Be(LlmCaptureTriageOutcome.Succeeded);
        result.Output!.Tasks[0].Title.Length.Should().Be(CaptureTriageOutputContract.MaxTaskTitleLength);
        result.Output.Tasks[0].Evidence.Length.Should().Be(CaptureTriageOutputContract.MaxTaskEvidenceLength);
    }

    [Fact]
    public async Task ExtractAsync_ShouldDedupeTitlesAndCapAtMaxTasks()
    {
        var tasks = string.Join(",", Enumerable.Range(0, 30).Select(i =>
            $$"""{"title":"Task {{i % 25}}","evidence":"evidence {{i}}"}"""));
        SetupCompletion($$"""{"tasks":[{{tasks}}]}""");
        var extractor = BuildExtractor();

        var result = await extractor.ExtractAsync(_userId, _boardId, TranscriptPayload());

        result.Outcome.Should().Be(LlmCaptureTriageOutcome.Succeeded);
        result.Output!.Tasks.Should().HaveCount(CaptureTriageOutputContract.MaxTasks);
        result.Output.Tasks.Select(t => t.Title).Should().OnlyHaveUniqueItems();
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
            .Setup(q => q.ReserveAsync(_userId, LlmSurface.CaptureTriage, It.IsAny<CancellationToken>()))
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
            q => q.CommitReservationAsync(_reservationId, "OpenAI", "gpt-4o-mini", 4096, 0, It.IsAny<CancellationToken>()),
            Times.Once);
        _quotaMock.Verify(
            q => q.ReleaseReservationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
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
            q => q.CommitReservationAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
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
            q => q.CommitReservationAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
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
    public async Task ExtractAsync_ShouldReturnInvalidOutput_WhenAllEntriesFailSanitization()
    {
        SetupCompletion("""{"tasks":[{"title":"   ","evidence":"x"},{"title":"ok","evidence":"  "}]}""");
        var extractor = BuildExtractor();

        var result = await extractor.ExtractAsync(_userId, _boardId, TranscriptPayload());

        // Entries were returned but none survived — malformed output (fallback), not an empty verdict.
        result.Outcome.Should().Be(LlmCaptureTriageOutcome.InvalidOutput);
    }

    [Fact]
    public async Task ExtractAsync_ShouldSendTriagePromptWithAttributionAndSettings()
    {
        ChatCompletionRequest? sent = null;
        _providerMock
            .Setup(p => p.CompleteAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ChatCompletionRequest, CancellationToken>((req, _) => sent = req)
            .ReturnsAsync(new LlmCompletionResult(
                """{"tasks":[{"title":"T","evidence":"E"}]}""",
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
        SetupCompletion("""{"tasks":[{"title":"T","evidence":"E"}]}""");
        var extractor = new LlmCaptureTriageExtractor(_providerMock.Object, _settings);

        var result = await extractor.ExtractAsync(_userId, _boardId, TranscriptPayload());

        result.Outcome.Should().Be(LlmCaptureTriageOutcome.Succeeded);
    }
}
