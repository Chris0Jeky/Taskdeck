using FluentAssertions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class TranscriptTriageChunkingTests
{
    [Fact]
    public void Chunk_ShouldKeepUnderBudgetTranscriptAsSingleChunk_EvenWithSpeakerTurns()
    {
        var transcript = """
            Alice: I will draft the launch notes.
            Bob: I will book the review meeting.
            Cara: I will share the decision log.
            """;

        var chunks = TranscriptTriageChunker.Chunk(transcript, maxInputTokens: 100, overlapTokens: 20);

        chunks.Should().ContainSingle();
        chunks[0].Offset.Should().Be(0);
        chunks[0].Text.Should().Be(transcript);
        chunks[0].EndOffset.Should().Be(transcript.Length);
        chunks[0].EstimatedTokens.Should().BeLessThanOrEqualTo(100);
    }

    [Fact]
    public void Chunk_ShouldPreferSpeakerAndBlankLineBoundaries_AndPreserveEveryCharacterWithBoundedOverlap()
    {
        var transcript = """
            Alice: We need to review the launch plan before Friday.
            Alice: I will prepare the decision log.

            Bob: Please book the room for the retrospective.
            Bob: I will send the invitation today.

            Cara: We should publish the notes after the meeting.
            """;

        var chunks = TranscriptTriageChunker.Chunk(transcript, maxInputTokens: 40, overlapTokens: 12);

        chunks.Should().HaveCountGreaterThan(1);
        chunks[0].EndOffset.Should().Be(transcript.IndexOf("Bob:", StringComparison.Ordinal));
        chunks.Should().OnlyContain(chunk => chunk.EstimatedTokens <= 40);

        for (var offset = 0; offset < transcript.Length; offset++)
        {
            chunks.Should().Contain(chunk => chunk.Offset <= offset && chunk.EndOffset > offset,
                $"source character at {offset} must be included by at least one map chunk");
        }

        for (var index = 1; index < chunks.Count; index++)
        {
            chunks[index].Offset.Should().BeGreaterThan(chunks[index - 1].Offset);
            chunks[index].Offset.Should().BeLessThan(chunks[index - 1].EndOffset,
                "the configured overlap should retain preceding speaker or blank-line context");
        }
    }

    [Fact]
    public void Chunk_ShouldHardSplitUnstructuredText_WhenNoPreferredBoundaryFits()
    {
        var transcript = new string('a', 200);

        var chunks = TranscriptTriageChunker.Chunk(transcript, maxInputTokens: 16, overlapTokens: 4);

        chunks.Should().HaveCountGreaterThan(1);
        chunks[0].EndOffset.Should().Be(48, "three ASCII word characters are conservatively budgeted as one token");
        chunks.Should().OnlyContain(chunk => chunk.EstimatedTokens <= 16);
        chunks.Select(chunk => chunk.Text).Should().OnlyContain(text => text.All(character => character == 'a'));
    }

    [Fact]
    public void EstimateTokens_ShouldBeMonotonicAndConservativeForPunctuationAndNonAsciiText()
    {
        var plain = "review the launch plan";
        var longer = plain + "! Please confirm by Friday.";
        var ascii = new string('a', 60);
        var nonAscii = new string('\u4e2d', 60);

        TranscriptTokenEstimator.EstimateTokens(longer).Should().BeGreaterThanOrEqualTo(
            TranscriptTokenEstimator.EstimateTokens(plain));
        TranscriptTokenEstimator.EstimateTokens(ascii).Should().BeGreaterThan(ascii.Length / 4,
            "the estimate must not use the under-budgeting text.Length / 4 shortcut");
        TranscriptTokenEstimator.EstimateTokens(nonAscii).Should().BeGreaterThan(
            TranscriptTokenEstimator.EstimateTokens(ascii),
            "non-ASCII transcript text needs a safer estimate than ASCII prose");
    }

    [Fact]
    public void ValidatePayload_ShouldAllowTwoHundredThousandCharacterTranscripts_WithoutRelaxingNormalCaptureBounds()
    {
        var acceptedTranscript = CaptureRequestContract.ValidatePayload(new CapturePayloadV1(
            CaptureRequestContract.CurrentSchemaVersion,
            CaptureSource.TranscriptPaste,
            new string('t', CaptureRequestContract.MaxTranscriptTextLength)));
        var rejectedTranscript = CaptureRequestContract.ValidatePayload(new CapturePayloadV1(
            CaptureRequestContract.CurrentSchemaVersion,
            CaptureSource.TranscriptFile,
            new string('t', CaptureRequestContract.MaxTranscriptTextLength + 1)));
        var rejectedNormalCapture = CaptureRequestContract.ValidatePayload(new CapturePayloadV1(
            CaptureRequestContract.CurrentSchemaVersion,
            CaptureSource.Typed,
            new string('t', CaptureRequestContract.MaxRawTextLength + 1)));

        acceptedTranscript.IsSuccess.Should().BeTrue();
        rejectedTranscript.IsSuccess.Should().BeFalse();
        rejectedNormalCapture.IsSuccess.Should().BeFalse();
    }
}
