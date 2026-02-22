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
}
