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

public class NotificationVisibilityTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<INotificationRepository> _notifications = new();
    private readonly Mock<IAuthorizationService> _authorization = new();
    private readonly NotificationService _service;

    public NotificationVisibilityTests()
    {
        _unitOfWork.SetupGet(unit => unit.Notifications).Returns(_notifications.Object);
        _unitOfWork.Setup(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _service = new NotificationService(_unitOfWork.Object, _authorization.Object);
    }

    [Fact]
    public async Task UnfilteredList_KeepsReadableAndBoardlessRowsInRepositoryOrder()
    {
        var readableBoard = Guid.NewGuid();
        var revokedBoard = Guid.NewGuid();
        var readable = NotificationFor(readableBoard);
        var revoked = NotificationFor(revokedBoard);
        var boardless = NotificationFor(null);
        var sameReadableBoard = NotificationFor(readableBoard);
        SetPage(readable, revoked, boardless, sameReadableBoard);
        using var cancellation = new CancellationTokenSource();
        _authorization.Setup(service => service.GetReadableBoardIdsAsync(
                _userId,
                It.Is<IEnumerable<Guid>>(ids => ids.Count() == 2 &&
                    ids.Contains(readableBoard) && ids.Contains(revokedBoard)),
                cancellation.Token))
            .ReturnsAsync(Result.Success<IReadOnlySet<Guid>>(new HashSet<Guid> { readableBoard }));

        var result = await _service.GetNotificationsAsync(
            _userId, new NotificationQueryDto(Limit: 20), cancellation.Token);

        result.IsSuccess.Should().BeTrue();
        result.Value.Select(item => item.Id).Should().Equal(readable.Id, boardless.Id, sameReadableBoard.Id);
        _authorization.Verify(service => service.GetReadableBoardIdsAsync(
            _userId, It.IsAny<IEnumerable<Guid>>(), cancellation.Token), Times.Once);
        _authorization.Verify(service => service.CanReadBoardAsync(It.IsAny<Guid>(), It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task UnfilteredList_FailsClosedWhenAuthorizationFails()
    {
        SetPage(NotificationFor(Guid.NewGuid()));
        _authorization.Setup(service => service.GetReadableBoardIdsAsync(
                _userId, It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<IReadOnlySet<Guid>>(ErrorCodes.UnexpectedError, "private failure detail"));

        var result = await _service.GetNotificationsAsync(_userId, new NotificationQueryDto(Limit: 20));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.ErrorMessage.Should().Be("You do not have access to notifications for this board");
        result.ErrorMessage.Should().NotContain("private failure detail");
    }

    [Fact]
    public async Task UnfilteredList_WithoutBoardRowsDoesNotQueryBoardAuthorization()
    {
        var boardless = NotificationFor(null);
        SetPage(boardless);

        var result = await _service.GetNotificationsAsync(_userId, new NotificationQueryDto(Limit: 20));

        result.IsSuccess.Should().BeTrue();
        result.Value.Select(item => item.Id).Should().Equal(boardless.Id);
        _authorization.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UnfilteredList_EmptyPageDoesNotQueryBoardAuthorization()
    {
        SetPage();
        var result = await _service.GetNotificationsAsync(_userId, new NotificationQueryDto(Limit: 20));
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
        _authorization.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UnfilteredList_RevalidatesInsteadOfCachingAnEarlierGrant()
    {
        var notification = NotificationFor(Guid.NewGuid());
        SetPage(notification);
        _authorization.SetupSequence(service => service.GetReadableBoardIdsAsync(
                _userId, It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlySet<Guid>>(new HashSet<Guid> { notification.BoardId!.Value }))
            .ReturnsAsync(Result.Success<IReadOnlySet<Guid>>(new HashSet<Guid>()));

        var before = await _service.GetNotificationsAsync(_userId, new NotificationQueryDto(Limit: 20));
        var after = await _service.GetNotificationsAsync(_userId, new NotificationQueryDto(Limit: 20));

        before.Value.Select(item => item.Id).Should().Equal(notification.Id);
        after.IsSuccess.Should().BeTrue();
        after.Value.Should().BeEmpty();
        _notifications.Verify(repository => repository.GetByUserIdAsync(
            _userId, 20, false, null, It.IsAny<CancellationToken>(), 0), Times.Exactly(2));
        // Do not refill an unauthorized page by scanning an unbounded history.
        _notifications.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task BoardFilteredList_PreservesTheExistingExplicitPermissionCheck()
    {
        var notification = NotificationFor(Guid.NewGuid());
        var boardId = notification.BoardId!.Value;
        _authorization.Setup(service => service.CanReadBoardAsync(_userId, boardId))
            .ReturnsAsync(Result.Success(true));
        _notifications.Setup(repository => repository.GetByUserIdAsync(
                _userId, 20, true, boardId, It.IsAny<CancellationToken>(), 0))
            .ReturnsAsync(new[] { notification });

        var result = await _service.GetNotificationsAsync(
            _userId, new NotificationQueryDto(UnreadOnly: true, BoardId: boardId, Limit: 20));

        result.IsSuccess.Should().BeTrue();
        result.Value.Select(item => item.Id).Should().Equal(notification.Id);
        _authorization.Verify(service => service.CanReadBoardAsync(_userId, boardId), Times.Once);
        _authorization.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MarkRead_RefusesAnOwnedNotificationWhenItsBoardIsUnavailable(bool lookupFails)
    {
        var notification = NotificationFor(Guid.NewGuid());
        _notifications.Setup(repository => repository.GetByIdAsync(notification.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(notification);
        _authorization.Setup(service => service.CanReadBoardAsync(_userId, notification.BoardId!.Value))
            .ReturnsAsync(lookupFails
                ? Result.Failure<bool>(ErrorCodes.NotFound, "private board detail")
                : Result.Success(false));

        var result = await _service.MarkAsReadAsync(_userId, notification.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.ErrorMessage.Should().Be("You do not have access to notifications for this board");
        notification.IsRead.Should().BeFalse();
        _unitOfWork.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MarkRead_AllowedBoardStillReturnsTheConfirmedReceipt()
    {
        var notification = NotificationFor(Guid.NewGuid());
        _notifications.Setup(repository => repository.GetByIdAsync(notification.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(notification);
        _authorization.Setup(service => service.CanReadBoardAsync(_userId, notification.BoardId!.Value))
            .ReturnsAsync(Result.Success(true));

        var result = await _service.MarkAsReadAsync(_userId, notification.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsRead.Should().BeTrue();
        _unitOfWork.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MarkRead_AnotherUsersRowIsRejectedBeforeBoardAuthorization()
    {
        var notification = NotificationFor(Guid.NewGuid());
        _notifications.Setup(repository => repository.GetByIdAsync(notification.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(notification);

        var result = await _service.MarkAsReadAsync(Guid.NewGuid(), notification.Id);

        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        notification.IsRead.Should().BeFalse();
        _authorization.VerifyNoOtherCalls();
        _unitOfWork.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MarkRead_BoardlessNotificationNeedsOnlyItsOwner()
    {
        var notification = NotificationFor(null);
        _notifications.Setup(repository => repository.GetByIdAsync(notification.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(notification);

        var result = await _service.MarkAsReadAsync(_userId, notification.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsRead.Should().BeTrue();
        _authorization.VerifyNoOtherCalls();
    }

    private Notification NotificationFor(Guid? boardId) => new(
        _userId, NotificationType.Mention, NotificationCadence.Immediate,
        "Synthetic mention", "Synthetic board detail", boardId);

    private void SetPage(params Notification[] notifications) =>
        _notifications.Setup(repository => repository.GetByUserIdAsync(
                _userId, 20, false, null, It.IsAny<CancellationToken>(), 0))
            .ReturnsAsync(notifications);
}
