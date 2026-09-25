using System.Text.Json;
using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class LlmCaptureTriageExtractorInvariantTests
{
    private readonly Mock<ILlmProvider> _provider = new();
    private readonly Mock<ILlmKillSwitchService> _killSwitch = new();
    private readonly Mock<ILlmQuotaService> _quota = new();
    private readonly LlmCaptureTriageSettings _settings = new();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _boardId = Guid.NewGuid();
    private readonly Guid _reservationId = Guid.NewGuid();

    public LlmCaptureTriageExtractorInvariantTests()
    {
        _provider
            .Setup(provider => provider.GetHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmHealthStatus(true, "OpenAI", Model: "gpt-4o-mini"));
        _killSwitch
            .Setup(service => service.IsKilledAsync(
                It.IsAny<LlmSurface?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _quota
            .Setup(service => service.ReserveAsync(
                It.IsAny<Guid>(),
                It.IsAny<LlmSurface>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QuotaReservationDto(
                true,
                null,
                _reservationId,
                100_000,
                60));
        _quota
            .Setup(service => service.CommitReservationAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<LlmSurface>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _quota
            .Setup(service => service.ReleaseReservationAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private LlmCaptureTriageExtractor BuildExtractor() => new(
        _provider.Object,
        _settings,
        _killSwitch.Object,
        _quota.Object);

    private static CapturePayloadV1 TranscriptPayload(string text) => new(
        CaptureRequestContract.CurrentSchemaVersion,
        CaptureSource.TranscriptPaste,
        text);

    private static LlmCompletionResult EmptyCompletion(
        string provider = "OpenAI",
        string model = "gpt-4o-mini",
        int tokensUsed = 25) => new(
        """{"tasks":[]}""",
        tokensUsed,
        IsActionable: false,
        Provider: provider,
        Model: model);

    private static LlmCompletionResult TaskCompletion(
        ChatCompletionRequest request,
        string provider,
        string model)
    {
        var evidenceQuote = request.Messages
            .Single()
            .Content
            .First(character => !char.IsWhiteSpace(character))
            .ToString();
        var content = JsonSerializer.Serialize(new
        {
            tasks = new[]
            {
                new
                {
                    title = "Send the launch notes",
                    type = "action",
                    assigneeHint = (string?)null,
                    dueDateHint = (string?)null,
                    confidence = 0.9m,
                    evidenceQuote
                }
            }
        });

        return new LlmCompletionResult(
            content,
            TokensUsed: 25,
            IsActionable: false,
            Provider: provider,
            Model: model);
    }

    private static string LongTranscript() => string.Join(
        "\n\n",
        Enumerable.Repeat(
            "Alice: I will send the launch notes after this meeting and confirm the final handoff.",
            8));

    [Fact]
    public async Task ExtractAsync_UsesOneCaptureReferenceDateAcrossEveryMapChunk()
    {
        _settings.MaxInputTokensPerChunk = 64;
        _settings.ChunkOverlapTokens = 16;
        var anchor = CaptureTriageAnchor.FromCapture(
            new DateTimeOffset(2026, 8, 29, 10, 0, 0, TimeSpan.Zero));
        var transcript = LongTranscript();
        var chunks = TranscriptTriageChunker.Chunk(
            transcript,
            _settings.MaxInputTokensPerChunk,
            _settings.ChunkOverlapTokens);
        var requests = new List<ChatCompletionRequest>();
        _provider
            .Setup(provider => provider.CompleteAsync(
                It.IsAny<ChatCompletionRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatCompletionRequest request, CancellationToken _) =>
            {
                requests.Add(request);
                return EmptyCompletion();
            });

        var result = await BuildExtractor().ExtractAsync(
            _userId,
            _boardId,
            TranscriptPayload(transcript),
            anchor);

        chunks.Should().HaveCountGreaterThan(1);
        result.Outcome.Should().Be(LlmCaptureTriageOutcome.EmptyExtraction);
        requests.Should().HaveCount(chunks.Count);
        var expectedPrompt = LlmCaptureTriagePrompt.BuildSystemPrompt(anchor.ReferenceDate);
        requests.Should().OnlyContain(request => request.SystemPrompt == expectedPrompt);
        requests.Select(request => request.SystemPrompt).Distinct().Should().ContainSingle();
    }

    [Theory]
    [InlineData("AzureOpenAI", "model-a")]
    [InlineData("OpenAI", "model-b")]
    public async Task ExtractAsync_ReturnsInvalidOutput_WhenSuccessfulMapChunksDisagreeOnProviderIdentity(
        string secondProvider,
        string secondModel)
    {
        _settings.MaxInputTokensPerChunk = 64;
        _settings.ChunkOverlapTokens = 16;
        var transcript = LongTranscript();
        var chunks = TranscriptTriageChunker.Chunk(
            transcript,
            _settings.MaxInputTokensPerChunk,
            _settings.ChunkOverlapTokens);
        var callCount = 0;
        _provider
            .Setup(provider => provider.CompleteAsync(
                It.IsAny<ChatCompletionRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatCompletionRequest request, CancellationToken _) =>
            {
                callCount++;
                return callCount == 1
                    ? TaskCompletion(request, "OpenAI", "model-a")
                    : TaskCompletion(request, secondProvider, secondModel);
            });

        var result = await BuildExtractor().ExtractAsync(
            _userId,
            _boardId,
            TranscriptPayload(transcript));

        chunks.Should().HaveCountGreaterThan(1);
        result.Outcome.Should().Be(LlmCaptureTriageOutcome.InvalidOutput);
        result.Output.Should().BeNull();
        result.Detail.Should().Be(
            "Chunked transcript extraction returned inconsistent provider provenance.");
        _provider.Verify(
            provider => provider.CompleteAsync(
                It.IsAny<ChatCompletionRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task ExtractAsync_RecommitsBilledTokens_WhenInitialQuotaCommitFails()
    {
        const int billedTokens = 250;
        _provider
            .Setup(provider => provider.CompleteAsync(
                It.IsAny<ChatCompletionRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyCompletion(tokensUsed: billedTokens));
        _quota
            .SetupSequence(service => service.CommitReservationAsync(
                _reservationId,
                _userId,
                LlmSurface.CaptureTriage,
                "OpenAI",
                "gpt-4o-mini",
                billedTokens,
                0,
                CancellationToken.None))
            .ThrowsAsync(new InvalidOperationException("Synthetic quota commit failure"))
            .Returns(Task.CompletedTask);
        var extractor = BuildExtractor();

        Func<Task> act = () => extractor.ExtractAsync(
            _userId,
            _boardId,
            TranscriptPayload("Alice: I will send the report by Friday."));

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("Synthetic quota commit failure");
        _quota.Verify(service => service.CommitReservationAsync(
            _reservationId,
            _userId,
            LlmSurface.CaptureTriage,
            "OpenAI",
            "gpt-4o-mini",
            billedTokens,
            0,
            CancellationToken.None), Times.Exactly(2));
        _quota.Verify(service => service.ReleaseReservationAsync(
            _reservationId,
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
