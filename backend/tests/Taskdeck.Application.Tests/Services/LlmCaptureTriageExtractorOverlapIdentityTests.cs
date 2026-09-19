using System.Text.Json;
using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class LlmCaptureTriageExtractorOverlapIdentityTests
{
    private readonly Mock<ILlmProvider> _provider = new();
    private readonly Mock<ILlmKillSwitchService> _killSwitch = new();
    private readonly Mock<ILlmQuotaService> _quota = new();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _boardId = Guid.NewGuid();
    private readonly Guid _reservationId = Guid.NewGuid();

    public LlmCaptureTriageExtractorOverlapIdentityTests()
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
            .ReturnsAsync(new QuotaReservationDto(true, null, _reservationId, 100_000, 60));
    }

    [Fact]
    public async Task ExtractAsync_ShouldKeepDistinctCrossChunkTasksThatShareOneEvidenceRange()
    {
        var settings = new LlmCaptureTriageSettings
        {
            MaxInputTokensPerChunk = 64,
            ChunkOverlapTokens = 16
        };
        var transcript = string.Join('|', Enumerable.Range(0, 40).Select(index => $"token{index:D3}"));
        var chunks = TranscriptTriageChunker.Chunk(
            transcript,
            settings.MaxInputTokensPerChunk,
            settings.ChunkOverlapTokens);
        chunks.Count.Should().BeGreaterThan(1);

        var overlapStart = Math.Max(chunks[0].Offset, chunks[1].Offset);
        var overlapEnd = Math.Min(chunks[0].EndOffset, chunks[1].EndOffset);
        overlapEnd.Should().BeGreaterThan(overlapStart);
        var rawOverlap = transcript[overlapStart..overlapEnd];
        var leadingWhitespace = rawOverlap.TakeWhile(char.IsWhiteSpace).Count();
        var trailingWhitespace = rawOverlap.Reverse().TakeWhile(char.IsWhiteSpace).Count();
        var quoteStart = overlapStart + leadingWhitespace;
        var quote = rawOverlap.Substring(
            leadingWhitespace,
            rawOverlap.Length - leadingWhitespace - trailingWhitespace);
        quote.Should().NotBeNullOrWhiteSpace();

        var matchingChunkCall = 0;
        _provider
            .Setup(provider => provider.CompleteAsync(
                It.IsAny<ChatCompletionRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatCompletionRequest request, CancellationToken _) =>
            {
                var requestText = request.Messages.Single().Content;
                var completion = requestText.Contains(quote, StringComparison.Ordinal)
                    ? V2ShapeCompletion((
                        matchingChunkCall++ == 0 ? "Prepare the agenda" : "Send the summary",
                        quote))
                    : V2ShapeCompletion();
                return new LlmCompletionResult(
                    completion,
                    100,
                    IsActionable: false,
                    Provider: "OpenAI",
                    Model: "gpt-4o-mini");
            });
        var extractor = new LlmCaptureTriageExtractor(
            _provider.Object,
            settings,
            _killSwitch.Object,
            _quota.Object);

        var result = await extractor.ExtractAsync(
            _userId,
            _boardId,
            new CapturePayloadV1(
                CaptureRequestContract.CurrentSchemaVersion,
                CaptureSource.TranscriptPaste,
                transcript));

        matchingChunkCall.Should().Be(2,
            "the exact absolute range must be visible in both adjacent overlap chunks");
        result.Outcome.Should().Be(LlmCaptureTriageOutcome.Succeeded);
        result.Output!.Tasks.Select(task => task.Title).Should().Equal(
            "Prepare the agenda",
            "Send the summary");
        result.EvidenceSpans.Should().HaveCount(2)
            .And.OnlyContain(span =>
                span.HasValue
                && span.Value.Start == quoteStart
                && span.Value.End == quoteStart + quote.Length);
    }

    private static string V2ShapeCompletion(params (string Title, string EvidenceQuote)[] tasks)
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
}
