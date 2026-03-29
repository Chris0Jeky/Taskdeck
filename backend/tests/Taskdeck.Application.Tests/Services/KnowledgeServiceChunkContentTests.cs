using FluentAssertions;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class KnowledgeServiceChunkContentTests
{
    private readonly Guid _documentId = Guid.NewGuid();

    [Fact]
    public void ChunkContent_SingleParagraphUnderChunkSize_ReturnsSingleChunk()
    {
        var content = "This is a short paragraph that fits within the chunk size limit.";

        var chunks = KnowledgeService.ChunkContent(_documentId, content);

        chunks.Should().HaveCount(1);
        chunks[0].DocumentId.Should().Be(_documentId);
        chunks[0].ChunkIndex.Should().Be(0);
        chunks[0].Content.Should().Be(content);
    }

    [Fact]
    public void ChunkContent_MultipleParagraphsFittingInOneChunk_ReturnsSingleChunk()
    {
        var content = "First paragraph.\n\nSecond paragraph.\n\nThird paragraph.";

        var chunks = KnowledgeService.ChunkContent(_documentId, content);

        chunks.Should().HaveCount(1);
        chunks[0].Content.Should().Contain("First paragraph.");
        chunks[0].Content.Should().Contain("Second paragraph.");
        chunks[0].Content.Should().Contain("Third paragraph.");
    }

    [Fact]
    public void ChunkContent_ParagraphBoundarySplitting_SplitsAtParagraphBoundary()
    {
        // Create content with multiple paragraphs that exceed chunk size (1000 chars)
        var paragraph = new string('a', 400);
        var content = $"{paragraph}\n\n{paragraph}\n\n{paragraph}\n\n{paragraph}";

        var chunks = KnowledgeService.ChunkContent(_documentId, content);

        chunks.Should().HaveCountGreaterThan(1);

        // All chunks should have correct document ID and sequential indices
        for (var i = 0; i < chunks.Count; i++)
        {
            chunks[i].DocumentId.Should().Be(_documentId);
            chunks[i].ChunkIndex.Should().Be(i);
            chunks[i].Content.Should().NotBeNullOrWhiteSpace();
        }

        // Each chunk should be at or under the chunk size
        foreach (var chunk in chunks)
        {
            chunk.Content.Length.Should().BeLessOrEqualTo(1000);
        }
    }

    [Fact]
    public void ChunkContent_SingleParagraphExceedingChunkSize_SplitsByCharacterBoundary()
    {
        // Create a single paragraph that exceeds the 1000-char chunk size
        var content = new string('x', 2500);

        var chunks = KnowledgeService.ChunkContent(_documentId, content);

        chunks.Should().HaveCount(3); // 1000 + 1000 + 500
        chunks[0].Content.Length.Should().Be(1000);
        chunks[1].Content.Length.Should().Be(1000);
        chunks[2].Content.Length.Should().Be(500);

        for (var i = 0; i < chunks.Count; i++)
        {
            chunks[i].ChunkIndex.Should().Be(i);
        }
    }

    [Fact]
    public void ChunkContent_EmptyContentAfterSplitting_ReturnsNoChunks()
    {
        // Content that is only whitespace/newlines between paragraph separators
        var content = "   \n\n   \n\n   ";

        var chunks = KnowledgeService.ChunkContent(_documentId, content);

        chunks.Should().BeEmpty();
    }

    [Fact]
    public void ChunkContent_MixedParagraphSizes_HandlesCorrectly()
    {
        var shortParagraph = "Short.";
        var mediumParagraph = new string('m', 500);
        var longParagraph = new string('L', 1500);

        var content = $"{shortParagraph}\n\n{mediumParagraph}\n\n{longParagraph}";

        var chunks = KnowledgeService.ChunkContent(_documentId, content);

        chunks.Should().HaveCountGreaterThanOrEqualTo(3);

        // First chunk should contain the short + medium paragraphs (combined < 1000)
        chunks[0].Content.Should().Contain("Short.");
        chunks[0].Content.Should().Contain(mediumParagraph);

        // Remaining chunks should contain the character-split long paragraph
        var longChunks = chunks.Skip(1).ToList();
        longChunks.Should().HaveCount(2); // 1000 + 500
        longChunks[0].Content.Length.Should().Be(1000);
        longChunks[1].Content.Length.Should().Be(500);
    }

    [Fact]
    public void ChunkContent_WindowsLineEndings_SplitsCorrectly()
    {
        var content = "First paragraph.\r\n\r\nSecond paragraph.";

        var chunks = KnowledgeService.ChunkContent(_documentId, content);

        chunks.Should().HaveCount(1);
        chunks[0].Content.Should().Contain("First paragraph.");
        chunks[0].Content.Should().Contain("Second paragraph.");
    }

    [Fact]
    public void ChunkContent_ExactChunkSizeParagraph_DoesNotProduceEmptyChunks()
    {
        var content = new string('e', 1000);

        var chunks = KnowledgeService.ChunkContent(_documentId, content);

        chunks.Should().HaveCount(1);
        chunks[0].Content.Length.Should().Be(1000);
        chunks.Should().OnlyContain(c => !string.IsNullOrWhiteSpace(c.Content));
    }
}
