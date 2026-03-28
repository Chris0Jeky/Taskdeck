using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class KnowledgeDocumentTests
{
    private readonly Guid _validUserId = Guid.NewGuid();

    [Fact]
    public void Constructor_ShouldCreateDocument_WithValidParameters()
    {
        var document = new KnowledgeDocument(
            _validUserId,
            "Test Title",
            "Test content for the knowledge document.",
            KnowledgeSourceType.Manual);

        document.Id.Should().NotBeEmpty();
        document.UserId.Should().Be(_validUserId);
        document.Title.Should().Be("Test Title");
        document.Content.Should().Be("Test content for the knowledge document.");
        document.SourceType.Should().Be(KnowledgeSourceType.Manual);
        document.BoardId.Should().BeNull();
        document.SourceUrl.Should().BeNull();
        document.Tags.Should().BeNull();
        document.IsArchived.Should().BeFalse();
    }

    [Fact]
    public void Constructor_ShouldCreateDocument_WithOptionalParameters()
    {
        var boardId = Guid.NewGuid();

        var document = new KnowledgeDocument(
            _validUserId,
            "Project Brief",
            "This is a project brief document.",
            KnowledgeSourceType.ProjectBrief,
            boardId,
            "https://example.com/brief",
            "planning,project");

        document.BoardId.Should().Be(boardId);
        document.SourceUrl.Should().Be("https://example.com/brief");
        document.Tags.Should().Be("planning,project");
        document.SourceType.Should().Be(KnowledgeSourceType.ProjectBrief);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenUserIdIsEmpty()
    {
        var act = () => new KnowledgeDocument(
            Guid.Empty,
            "Title",
            "Content",
            KnowledgeSourceType.Manual);

        act.Should().Throw<DomainException>()
            .WithMessage("User ID cannot be empty")
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_ShouldThrow_WhenTitleIsEmpty(string? title)
    {
        var act = () => new KnowledgeDocument(
            _validUserId,
            title!,
            "Content",
            KnowledgeSourceType.Manual);

        act.Should().Throw<DomainException>()
            .WithMessage("Title cannot be empty")
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenTitleExceedsMaxLength()
    {
        var longTitle = new string('a', 201);

        var act = () => new KnowledgeDocument(
            _validUserId,
            longTitle,
            "Content",
            KnowledgeSourceType.Manual);

        act.Should().Throw<DomainException>()
            .WithMessage("Title cannot exceed 200 characters")
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_ShouldThrow_WhenContentIsEmpty(string? content)
    {
        var act = () => new KnowledgeDocument(
            _validUserId,
            "Title",
            content!,
            KnowledgeSourceType.Manual);

        act.Should().Throw<DomainException>()
            .WithMessage("Content cannot be empty")
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenContentExceedsMaxLength()
    {
        var longContent = new string('a', 50001);

        var act = () => new KnowledgeDocument(
            _validUserId,
            "Title",
            longContent,
            KnowledgeSourceType.Manual);

        act.Should().Throw<DomainException>()
            .WithMessage("Content cannot exceed 50000 characters")
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Update_ShouldUpdateTitleAndContent()
    {
        var document = new KnowledgeDocument(
            _validUserId,
            "Original Title",
            "Original content.",
            KnowledgeSourceType.Manual);

        var originalUpdatedAt = document.UpdatedAt;

        document.Update("Updated Title", "Updated content.", "new-tag");

        document.Title.Should().Be("Updated Title");
        document.Content.Should().Be("Updated content.");
        document.Tags.Should().Be("new-tag");
        document.UpdatedAt.Should().BeOnOrAfter(originalUpdatedAt);
    }

    [Fact]
    public void Update_ShouldThrow_WhenDocumentIsArchived()
    {
        var document = new KnowledgeDocument(
            _validUserId,
            "Title",
            "Content",
            KnowledgeSourceType.Manual);

        document.Archive();

        var act = () => document.Update("New Title", "New Content");

        act.Should().Throw<DomainException>()
            .WithMessage("Archived documents cannot be edited")
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Archive_ShouldSetIsArchivedToTrue()
    {
        var document = new KnowledgeDocument(
            _validUserId,
            "Title",
            "Content",
            KnowledgeSourceType.Manual);

        document.Archive();

        document.IsArchived.Should().BeTrue();
    }

    [Fact]
    public void Archive_ShouldBeIdempotent()
    {
        var document = new KnowledgeDocument(
            _validUserId,
            "Title",
            "Content",
            KnowledgeSourceType.Manual);

        document.Archive();
        var updatedAt = document.UpdatedAt;

        document.Archive();

        document.IsArchived.Should().BeTrue();
        document.UpdatedAt.Should().Be(updatedAt);
    }

    [Fact]
    public void Unarchive_ShouldSetIsArchivedToFalse()
    {
        var document = new KnowledgeDocument(
            _validUserId,
            "Title",
            "Content",
            KnowledgeSourceType.Manual);

        document.Archive();
        document.Unarchive();

        document.IsArchived.Should().BeFalse();
    }

    [Fact]
    public void Unarchive_ShouldBeIdempotent_WhenNotArchived()
    {
        var document = new KnowledgeDocument(
            _validUserId,
            "Title",
            "Content",
            KnowledgeSourceType.Manual);

        var updatedAt = document.UpdatedAt;

        document.Unarchive();

        document.IsArchived.Should().BeFalse();
        document.UpdatedAt.Should().Be(updatedAt);
    }
}
