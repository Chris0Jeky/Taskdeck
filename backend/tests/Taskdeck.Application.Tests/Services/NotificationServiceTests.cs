using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class NotificationServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<INotificationRepository> _notificationRepositoryMock = new();
    private readonly Mock<INotificationPreferenceRepository> _preferenceRepositoryMock = new();
    private readonly Mock<IAuthorizationService> _authorizationServiceMock = new();
    private readonly NotificationService _service;

    public NotificationServiceTests()
    {
        _unitOfWorkMock.SetupGet(u => u.Notifications).Returns(_notificationRepositoryMock.Object);
        _unitOfWorkMock.SetupGet(u => u.NotificationPreferences).Returns(_preferenceRepositoryMock.Object);
        _unitOfWorkMock.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        _notificationRepositoryMock
            .Setup(r => r.AddAsync(It.IsAny<Notification>(), default))
            .ReturnsAsync((Notification notification, CancellationToken _) => notification);
        _preferenceRepositoryMock
            .Setup(r => r.AddAsync(It.IsAny<NotificationPreference>(), default))
            .ReturnsAsync((NotificationPreference preference, CancellationToken _) => preference);

        _service = new NotificationService(_unitOfWorkMock.Object, _authorizationServiceMock.Object);
    }

    [Fact]
    public async Task PublishAsync_ShouldCreateNotification_WhenPreferenceAllowsEventType()
    {
        var userId = Guid.NewGuid();
        var preference = new NotificationPreference(
            userId,
            inAppChannelEnabled: true,
            mentionImmediateEnabled: true,
            mentionDigestEnabled: false,
            assignmentImmediateEnabled: true,
            assignmentDigestEnabled: false,
            proposalOutcomeImmediateEnabled: true,
            proposalOutcomeDigestEnabled: false);

        _preferenceRepositoryMock
            .Setup(r => r.GetByUserIdAsync(userId, default))
            .ReturnsAsync(preference);
        _notificationRepositoryMock
            .Setup(r => r.GetByUserAndDeduplicationKeyAsync(userId, "dedupe-1", default))
            .ReturnsAsync((Notification?)null);

        var result = await _service.PublishAsync(new CreateNotificationRequestDto(
            userId,
            NotificationType.Mention,
            "Mentioned",
            "You were mentioned",
            DeduplicationKey: "dedupe-1"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
        _notificationRepositoryMock.Verify(r => r.AddAsync(It.IsAny<Notification>(), default), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_ShouldSkip_WhenPreferenceDisablesEventType()
    {
        var userId = Guid.NewGuid();
        var preference = new NotificationPreference(
            userId,
            inAppChannelEnabled: true,
            mentionImmediateEnabled: true,
            mentionDigestEnabled: false,
            assignmentImmediateEnabled: false,
            assignmentDigestEnabled: false,
            proposalOutcomeImmediateEnabled: true,
            proposalOutcomeDigestEnabled: false);

        _preferenceRepositoryMock
            .Setup(r => r.GetByUserIdAsync(userId, default))
            .ReturnsAsync(preference);

        var result = await _service.PublishAsync(new CreateNotificationRequestDto(
            userId,
            NotificationType.Assignment,
            "Assignment",
            "You were assigned"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
        _notificationRepositoryMock.Verify(r => r.AddAsync(It.IsAny<Notification>(), default), Times.Never);
    }

    [Fact]
    public async Task PublishAsync_ShouldSkipDuplicate_WhenDeduplicationKeyAlreadyExists()
    {
        var userId = Guid.NewGuid();
        var preference = NotificationPreference.CreateDefault(userId);
        var existing = new Notification(
            userId,
            NotificationType.Mention,
            NotificationCadence.Immediate,
            "Existing",
            "Already here",
            deduplicationKey: "dup-key");

        _preferenceRepositoryMock
            .Setup(r => r.GetByUserIdAsync(userId, default))
            .ReturnsAsync(preference);
        _notificationRepositoryMock
            .Setup(r => r.GetByUserAndDeduplicationKeyAsync(userId, "dup-key", default))
            .ReturnsAsync(existing);

        var result = await _service.PublishAsync(new CreateNotificationRequestDto(
            userId,
            NotificationType.Mention,
            "Mentioned",
            "You were mentioned",
            DeduplicationKey: "dup-key"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
        _notificationRepositoryMock.Verify(r => r.AddAsync(It.IsAny<Notification>(), default), Times.Never);
    }

    [Fact]
    public async Task GetNotificationsAsync_ShouldReturnForbidden_WhenBoardAccessDenied()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();

        _authorizationServiceMock
            .Setup(s => s.CanReadBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Success(false));

        var result = await _service.GetNotificationsAsync(
            userId,
            new NotificationQueryDto(UnreadOnly: true, BoardId: boardId, Limit: 20));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task GetNotificationsAsync_ShouldReturnForbidden_WhenBoardLookupFails()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();

        _authorizationServiceMock
            .Setup(s => s.CanReadBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Failure<bool>(ErrorCodes.NotFound, "board missing"));

        var result = await _service.GetNotificationsAsync(
            userId,
            new NotificationQueryDto(UnreadOnly: false, BoardId: boardId, Limit: 20));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.ErrorMessage.Should().Be("You do not have access to notifications for this board");
    }

    [Fact]
    public async Task MarkAllAsReadAsync_ShouldMarkAllUnread_WhenNotificationsExist()
    {
        var userId = Guid.NewGuid();
        var n1 = new Notification(userId, NotificationType.Mention, NotificationCadence.Immediate, "N1", "Message 1");
        var n2 = new Notification(userId, NotificationType.Assignment, NotificationCadence.Immediate, "N2", "Message 2");

        _notificationRepositoryMock
            .Setup(r => r.GetUnreadByUserIdAsync(userId, null, default))
            .ReturnsAsync(new[] { n1, n2 });

        var result = await _service.MarkAllAsReadAsync(userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(2);
        n1.IsRead.Should().BeTrue();
        n2.IsRead.Should().BeTrue();
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task MarkAllAsReadAsync_ShouldReturnZero_WhenNoUnreadNotifications()
    {
        var userId = Guid.NewGuid();
        _notificationRepositoryMock
            .Setup(r => r.GetUnreadByUserIdAsync(userId, null, default))
            .ReturnsAsync(Array.Empty<Notification>());

        var result = await _service.MarkAllAsReadAsync(userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task MarkAllAsReadAsync_ShouldReturnForbidden_WhenBoardAccessDenied()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();

        _authorizationServiceMock
            .Setup(s => s.CanReadBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Success(false));

        var result = await _service.MarkAllAsReadAsync(userId, boardId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task MarkAllAsReadAsync_ShouldReturnValidationError_WhenUserIdEmpty()
    {
        var result = await _service.MarkAllAsReadAsync(Guid.Empty);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task PublishAsync_ShouldAvoidDuplicatesWithinSameUnitOfWork_WhenPreferenceIsNotPersistedYet()
    {
        var userId = Guid.NewGuid();
        _preferenceRepositoryMock
            .Setup(r => r.GetByUserIdAsync(userId, default))
            .ReturnsAsync((NotificationPreference?)null);
        _notificationRepositoryMock
            .Setup(r => r.GetByUserAndDeduplicationKeyAsync(userId, "dup-key", default))
            .ReturnsAsync((Notification?)null);

        var firstResult = await _service.PublishAsync(new CreateNotificationRequestDto(
            userId,
            NotificationType.Mention,
            "Mentioned",
            "You were mentioned",
            DeduplicationKey: "dup-key"));

        var secondResult = await _service.PublishAsync(new CreateNotificationRequestDto(
            userId,
            NotificationType.Mention,
            "Mentioned again",
            "You were mentioned again",
            DeduplicationKey: "dup-key"));

        firstResult.IsSuccess.Should().BeTrue();
        firstResult.Value.Should().BeTrue();
        secondResult.IsSuccess.Should().BeTrue();
        secondResult.Value.Should().BeFalse();
        _preferenceRepositoryMock.Verify(r => r.AddAsync(It.IsAny<NotificationPreference>(), default), Times.Once);
        _notificationRepositoryMock.Verify(r => r.AddAsync(It.IsAny<Notification>(), default), Times.Once);
    }
}
