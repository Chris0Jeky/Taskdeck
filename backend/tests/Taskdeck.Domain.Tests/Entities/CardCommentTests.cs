using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class CardCommentTests
{
    private readonly Guid _cardId = Guid.NewGuid();
    private readonly Guid _boardId = Guid.NewGuid();
    private readonly Guid _authorUserId = Guid.NewGuid();

    [Fact]
    public void Constructor_ShouldCreateComment_WithValidData()
    {
        // Arrange
        var parentCommentId = Guid.NewGuid();

        // Act
        var comment = new CardComment(_cardId, _boardId, _authorUserId, "Looks good", parentCommentId);

        // Assert
        comment.CardId.Should().Be(_cardId);
        comment.BoardId.Should().Be(_boardId);
        comment.AuthorUserId.Should().Be(_authorUserId);
        comment.ParentCommentId.Should().Be(parentCommentId);
        comment.Content.Should().Be("Looks good");
        comment.IsDeleted.Should().BeFalse();
        comment.DeletedAt.Should().BeNull();
        comment.EditedAt.Should().BeNull();
        comment.Mentions.Should().BeEmpty();
        comment.Replies.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_ShouldInitializeDefaultEntityValues()
    {
        // Arrange
        var before = DateTimeOffset.UtcNow;

        // Act
        var comment = new CardComment(_cardId, _boardId, _authorUserId, "Initial content");

        // Assert
        comment.Id.Should().NotBe(Guid.Empty);
        comment.CreatedAt.Should().BeOnOrAfter(before);
        comment.UpdatedAt.Should().BeOnOrAfter(comment.CreatedAt);
        comment.ParentCommentId.Should().BeNull();
        comment.IsDeleted.Should().BeFalse();
        comment.DeletedAt.Should().BeNull();
        comment.EditedAt.Should().BeNull();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenCardIdIsEmpty()
    {
        // Act
        var act = () => new CardComment(Guid.Empty, _boardId, _authorUserId, "Content");

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Card ID cannot be empty");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenBoardIdIsEmpty()
    {
        // Act
        var act = () => new CardComment(_cardId, Guid.Empty, _authorUserId, "Content");

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Board ID cannot be empty");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenAuthorUserIdIsEmpty()
    {
        // Act
        var act = () => new CardComment(_cardId, _boardId, Guid.Empty, "Content");

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Author user ID cannot be empty");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenParentCommentIdIsEmptyGuid()
    {
        // Act
        var act = () => new CardComment(_cardId, _boardId, _authorUserId, "Content", Guid.Empty);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Parent comment ID cannot be empty");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Constructor_ShouldThrow_WhenContentIsBlank(string content)
    {
        // Act
        var act = () => new CardComment(_cardId, _boardId, _authorUserId, content);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Comment content cannot be empty");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenContentExceedsMaxLength()
    {
        // Arrange
        var content = new string('a', 4001);

        // Act
        var act = () => new CardComment(_cardId, _boardId, _authorUserId, content);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Comment content cannot exceed 4000 characters");
    }

    [Fact]
    public void UpdateContent_ShouldUpdateContentAndEditedMetadata()
    {
        // Arrange
        var comment = new CardComment(_cardId, _boardId, _authorUserId, "Original");
        var originalUpdatedAt = comment.UpdatedAt;

        // Act
        comment.UpdateContent("Updated content");

        // Assert
        comment.Content.Should().Be("Updated content");
        comment.EditedAt.Should().NotBeNull();
        comment.UpdatedAt.Should().BeOnOrAfter(originalUpdatedAt);
        comment.EditedAt.Should().BeOnOrAfter(comment.CreatedAt);
    }

    [Fact]
    public void UpdateContent_ShouldThrow_WhenCommentIsDeleted()
    {
        // Arrange
        var comment = new CardComment(_cardId, _boardId, _authorUserId, "Original");
        comment.SoftDelete();

        // Act
        var act = () => comment.UpdateContent("Updated content");

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Deleted comments cannot be edited");
    }

    [Fact]
    public void SoftDelete_ShouldMarkCommentDeleted_AndClearMentions()
    {
        // Arrange
        var comment = new CardComment(_cardId, _boardId, _authorUserId, "Original");
        comment.ReplaceMentions(
            [
                (Guid.NewGuid(), "alice"),
                (Guid.NewGuid(), "bob")
            ]);

        // Act
        comment.SoftDelete();

        // Assert
        comment.IsDeleted.Should().BeTrue();
        comment.Content.Should().Be("[deleted]");
        comment.DeletedAt.Should().NotBeNull();
        comment.EditedAt.Should().Be(comment.DeletedAt);
        comment.Mentions.Should().BeEmpty();
    }

    [Fact]
    public void SoftDelete_ShouldBeIdempotent()
    {
        // Arrange
        var comment = new CardComment(_cardId, _boardId, _authorUserId, "Original");
        comment.SoftDelete();
        var deletedAt = comment.DeletedAt;
        var editedAt = comment.EditedAt;
        var updatedAt = comment.UpdatedAt;

        // Act
        comment.SoftDelete();

        // Assert
        comment.DeletedAt.Should().Be(deletedAt);
        comment.EditedAt.Should().Be(editedAt);
        comment.UpdatedAt.Should().Be(updatedAt);
        comment.Content.Should().Be("[deleted]");
    }

    [Fact]
    public void ReplaceMentions_ShouldReplaceMentions_AndFilterInvalidOrDuplicateEntries()
    {
        // Arrange
        var comment = new CardComment(_cardId, _boardId, _authorUserId, "Original");
        var aliceId = Guid.NewGuid();
        var bobId = Guid.NewGuid();
        var originalUpdatedAt = comment.UpdatedAt;

        // Act
        comment.ReplaceMentions(
            [
                (aliceId, "alice"),
                (aliceId, "alice-duplicate"),
                (Guid.Empty, "invalid"),
                (bobId, ""),
                (bobId, "bob")
            ]);

        // Assert
        comment.Mentions.Should().HaveCount(2);
        comment.Mentions.Select(m => m.CardCommentId).Should().OnlyContain(id => id == comment.Id);
        comment.Mentions.Select(m => m.MentionedUserId).Should().BeEquivalentTo([aliceId, bobId]);
        comment.Mentions.Select(m => m.MentionedUsername).Should().BeEquivalentTo(["alice", "bob"]);
        comment.UpdatedAt.Should().BeOnOrAfter(originalUpdatedAt);
    }
}
