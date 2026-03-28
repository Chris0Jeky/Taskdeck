using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class KnowledgeChunkTests
{
    private readonly Guid _validDocumentId = Guid.NewGuid();

    [Fact]
    public void Constructor_ShouldCreateChunk_WithValidParameters()
    {
        var chunk = new KnowledgeChunk(
            _validDocumentId,
            0,
            "This is chunk content.");

        chunk.Id.Should().NotBeEmpty();
        chunk.DocumentId.Should().Be(_validDocumentId);
        chunk.ChunkIndex.Should().Be(0);
        chunk.Content.Should().Be("This is chunk content.");
        chunk.Metadata.Should().BeNull();
    }

    [Fact]
    public void Constructor_ShouldCreateChunk_WithMetadata()
    {
        var metadata = "{\"paragraph\": 1}";

        var chunk = new KnowledgeChunk(
            _validDocumentId,
            2,
            "Content with metadata.",
            metadata);

        chunk.Metadata.Should().Be(metadata);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenDocumentIdIsEmpty()
    {
        var act = () => new KnowledgeChunk(
            Guid.Empty,
            0,
            "Content");

        act.Should().Throw<DomainException>()
            .WithMessage("Document ID cannot be empty")
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenChunkIndexIsNegative()
    {
        var act = () => new KnowledgeChunk(
            _validDocumentId,
            -1,
            "Content");

        act.Should().Throw<DomainException>()
            .WithMessage("Chunk index must be non-negative")
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_ShouldThrow_WhenContentIsEmpty(string? content)
    {
        var act = () => new KnowledgeChunk(
            _validDocumentId,
            0,
            content!);

        act.Should().Throw<DomainException>()
            .WithMessage("Chunk content cannot be empty")
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenMetadataExceedsMaxLength()
    {
        var longMetadata = new string('a', 4001);

        var act = () => new KnowledgeChunk(
            _validDocumentId,
            0,
            "Content",
            longMetadata);

        act.Should().Throw<DomainException>()
            .WithMessage("Metadata cannot exceed 4000 characters")
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldAcceptZeroChunkIndex()
    {
        var chunk = new KnowledgeChunk(
            _validDocumentId,
            0,
            "Content");

        chunk.ChunkIndex.Should().Be(0);
    }
}
