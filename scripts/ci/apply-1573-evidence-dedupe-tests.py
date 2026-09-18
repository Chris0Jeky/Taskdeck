from pathlib import Path

path = Path("backend/tests/Taskdeck.Application.Tests/Services/LlmCaptureTriageExtractorTests.cs")
text = path.read_text(encoding="utf-8")
anchor = """    [Fact]
    public async Task ExtractAsync_ShouldReportProgressBeforeAndAfterEachMapCompletion()
"""
if text.count(anchor) != 1:
    raise SystemExit(f"expected one insertion anchor, found {text.count(anchor)}")

addition = r'''    [Fact]
    public async Task ExtractAsync_ShouldDedupeRephrasedOverlapTaskByAbsoluteEvidenceSpan()
    {
        _settings.MaxInputTokensPerChunk = 64;
        _settings.ChunkOverlapTokens = 16;
        var transcript = string.Join('|', Enumerable.Range(0, 40).Select(index => $"token{index:D3}"));
        var chunks = TranscriptTriageChunker.Chunk(
            transcript,
            _settings.MaxInputTokensPerChunk,
            _settings.ChunkOverlapTokens);
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
        _providerMock
            .Setup(provider => provider.CompleteAsync(
                It.IsAny<ChatCompletionRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatCompletionRequest request, CancellationToken _) =>
            {
                var requestText = request.Messages.Single().Content;
                var completion = requestText.Contains(quote, StringComparison.Ordinal)
                    ? V2ShapeCompletion((
                        matchingChunkCall++ == 0 ? "Prepare the launch packet" : "Finalize the launch handoff",
                        quote))
                    : V2ShapeCompletion();
                return new LlmCompletionResult(
                    completion,
                    100,
                    IsActionable: false,
                    Provider: "OpenAI",
                    Model: "gpt-4o-mini");
            });

        var result = await BuildExtractor().ExtractAsync(
            _userId,
            _boardId,
            TranscriptPayload(transcript));

        matchingChunkCall.Should().Be(2,
            "the exact absolute range must be visible only in the two adjacent overlap chunks");
        result.Outcome.Should().Be(LlmCaptureTriageOutcome.Succeeded);
        result.Output!.Tasks.Should().ContainSingle();
        result.Output.Tasks[0].Title.Should().Be("Prepare the launch packet",
            "the reducer keeps its existing first-stable-task contract");
        result.EvidenceSpans.Should().ContainSingle()
            .Which.Should().Be((quoteStart, quoteStart + quote.Length));
    }

    [Fact]
    public async Task ExtractAsync_ShouldKeepDistinctSameChunkTasksThatShareOneEvidenceRange()
    {
        const string transcript = "Alice: prepare the agenda and send the summary.";
        SetupCompletion(V2ShapeCompletion(
            ("Prepare the agenda", transcript),
            ("Send the summary", transcript)));

        var result = await BuildExtractor().ExtractAsync(
            _userId,
            _boardId,
            TranscriptPayload(transcript));

        result.Outcome.Should().Be(LlmCaptureTriageOutcome.Succeeded);
        result.Output!.Tasks.Select(task => task.Title).Should().Equal(
            "Prepare the agenda",
            "Send the summary");
        result.EvidenceSpans.Should().HaveCount(2)
            .And.OnlyContain(span => span == (0, transcript.Length));
    }

    [Fact]
    public async Task ExtractAsync_ShouldNotEvidenceDedupeAmbiguousNullSpans()
    {
        const string transcript = "repeat and repeat";
        const string quote = "repeat";
        SetupCompletion(V2ShapeCompletion(
            ("Review the first occurrence", quote),
            ("Review the second occurrence", quote)));

        var result = await BuildExtractor().ExtractAsync(
            _userId,
            _boardId,
            TranscriptPayload(transcript));

        result.Outcome.Should().Be(LlmCaptureTriageOutcome.Succeeded);
        result.Output!.Tasks.Should().HaveCount(2);
        result.EvidenceSpans.Should().HaveCount(2).And.OnlyContain(span => span is null);
    }

'''
path.write_text(text.replace(anchor, addition + anchor, 1), encoding="utf-8")
