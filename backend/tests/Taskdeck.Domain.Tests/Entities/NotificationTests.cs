using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class NotificationTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _boardId = Guid.NewGuid();
    private readonly Guid _sourceEntityId = Guid.NewGuid();

    [Fact]
    public void Constructor_ShouldCreateNotification_WithValidData()
    {
        // Arrange & Act
        var notification = new Notification(
            _userId,
            NotificationType.Mention,
            NotificationCadence.Immediate,
            "Mentioned in comment",
            "You were mentioned on a card.",
            _boardId,
            "CardComment",
            _sourceEntityId,
            "mention:123");

        // Assert
        notification.UserId.Should().Be(_userId);
        notification.BoardId.Should().Be(_boardId);
        notification.Type.Should().Be(NotificationType.Mention);
        notification.Cadence.Should().Be(NotificationCadence.Immediate);
        notification.Title.Should().Be("Mentioned in comment");
        notification.Message.Should().Be("You were mentioned on a card.");
        notification.SourceEntityType.Should().Be("CardComment");
        notification.SourceEntityId.Should().Be(_sourceEntityId);
        notification.DeduplicationKey.Should().Be("mention:123");
        notification.IsRead.Should().BeFalse();
        notification.ReadAt.Should().BeNull();
    }

    [Fact]
    public void Constructor_ShouldInitializeDefaultValues()
    {
        // Arrange
        var before = DateTimeOffset.UtcNow;

        // Act
        var notification = new Notification(
            _userId,
            NotificationType.Assignment,
            NotificationCadence.Digest,
            "Assigned card",
            "A card was assigned to you.");

        // Assert
        notification.Id.Should().NotBe(Guid.Empty);
        notification.CreatedAt.Should().BeOnOrAfter(before);
        notification.UpdatedAt.Should().BeOnOrAfter(notification.CreatedAt);
        notification.BoardId.Should().BeNull();
        notification.SourceEntityType.Should().BeNull();
        notification.SourceEntityId.Should().BeNull();
        notification.DeduplicationKey.Should().BeNull();
        notification.IsRead.Should().BeFalse();
        notification.ReadAt.Should().BeNull();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenUserIdIsEmpty()
    {
        // Act
        var act = () => new Notification(
            Guid.Empty,
            NotificationType.Mention,
            NotificationCadence.Immediate,
            "Title",
            "Message");

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("User ID cannot be empty");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Constructor_ShouldThrow_WhenTitleIsBlank(string title)
    {
        // Act
        var act = () => new Notification(
            _userId,
            NotificationType.Mention,
            NotificationCadence.Immediate,
            title,
            "Message");

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Notification title cannot be empty");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenTitleIsTooLong()
    {
        // Arrange
        var title = new string('a', 161);

        // Act
        var act = () => new Notification(
            _userId,
            NotificationType.Mention,
            NotificationCadence.Immediate,
            title,
            "Message");

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Notification title cannot exceed 160 characters");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Constructor_ShouldThrow_WhenMessageIsBlank(string message)
    {
        // Act
        var act = () => new Notification(
            _userId,
            NotificationType.Mention,
            NotificationCadence.Immediate,
            "Title",
            message);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Notification message cannot be empty");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenMessageIsTooLong()
    {
        // Arrange
        var message = new string('a', 2001);

        // Act
        var act = () => new Notification(
            _userId,
            NotificationType.Mention,
            NotificationCadence.Immediate,
            "Title",
            message);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Notification message cannot exceed 2000 characters");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenSourceEntityTypeIsTooLong()
    {
        // Arrange
        var sourceEntityType = new string('a', 51);

        // Act
        var act = () => new Notification(
            _userId,
            NotificationType.Mention,
            NotificationCadence.Immediate,
            "Title",
            "Message",
            sourceEntityType: sourceEntityType);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Source entity type cannot exceed 50 characters");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenDeduplicationKeyIsTooLong()
    {
        // Arrange
        var deduplicationKey = new string('a', 201);

        // Act
        var act = () => new Notification(
            _userId,
            NotificationType.Mention,
            NotificationCadence.Immediate,
            "Title",
            "Message",
            deduplicationKey: deduplicationKey);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Deduplication key cannot exceed 200 characters");
    }

    [Fact]
    public void MarkAsRead_ShouldSetReadState()
    {
        // Arrange
        var notification = new Notification(
            _userId,
            NotificationType.ProposalOutcome,
            NotificationCadence.Immediate,
            "Proposal approved",
            "Your proposal was approved.");
        var originalUpdatedAt = notification.UpdatedAt;

        // Act
        notification.MarkAsRead();

        // Assert
        notification.IsRead.Should().BeTrue();
        notification.ReadAt.Should().NotBeNull();
        notification.UpdatedAt.Should().BeOnOrAfter(originalUpdatedAt);
    }

    [Fact]
    public void MarkAsRead_ShouldBeIdempotent()
    {
        // Arrange
        var notification = new Notification(
            _userId,
            NotificationType.ProposalOutcome,
            NotificationCadence.Immediate,
            "Proposal approved",
            "Your proposal was approved.");
        notification.MarkAsRead();
        var readAt = notification.ReadAt;
        var updatedAt = notification.UpdatedAt;

        // Act
        notification.MarkAsRead();

        // Assert
        notification.IsRead.Should().BeTrue();
        notification.ReadAt.Should().Be(readAt);
        notification.UpdatedAt.Should().Be(updatedAt);
    }

    [Fact]
    public void MarkAsUnread_ShouldClearReadState()
    {
        // Arrange
        var notification = new Notification(
            _userId,
            NotificationType.ProposalOutcome,
            NotificationCadence.Immediate,
            "Proposal approved",
            "Your proposal was approved.");
        notification.MarkAsRead();
        var readUpdatedAt = notification.UpdatedAt;

        // Act
        notification.MarkAsUnread();

        // Assert
        notification.IsRead.Should().BeFalse();
        notification.ReadAt.Should().BeNull();
        notification.UpdatedAt.Should().BeOnOrAfter(readUpdatedAt);
    }

    [Fact]
    public void MarkAsUnread_ShouldBeIdempotent_WhenAlreadyUnread()
    {
        // Arrange
        var notification = new Notification(
            _userId,
            NotificationType.Assignment,
            NotificationCadence.Digest,
            "Assigned card",
            "A card was assigned to you.");
        var originalUpdatedAt = notification.UpdatedAt;

        // Act
        notification.MarkAsUnread();

        // Assert
        notification.IsRead.Should().BeFalse();
        notification.ReadAt.Should().BeNull();
        notification.UpdatedAt.Should().Be(originalUpdatedAt);
    }
}
