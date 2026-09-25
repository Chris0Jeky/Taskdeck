using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public sealed class BoardAccessOwnerProtectionTests
{
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IBoardAccessRepository> _accesses = new();
    private readonly Mock<IBoardRepository> _boards = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly BoardAccessService _service;

    public BoardAccessOwnerProtectionTests()
    {
        _unitOfWork.Setup(u => u.BoardAccesses).Returns(_accesses.Object);
        _unitOfWork.Setup(u => u.Boards).Returns(_boards.Object);
        _unitOfWork.Setup(u => u.Users).Returns(_users.Object);
        _notifications.Setup(n => n.PublishAsync(It.IsAny<CreateNotificationRequestDto>(), default))
            .ReturnsAsync(Result.Success(true));
        _service = new BoardAccessService(_unitOfWork.Object, _notifications.Object);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Admin_CannotDemoteOrRevokeOwnerAccess(bool revoke, bool targetIsPrimaryOwner)
    {
        var (board, actor, access) = Arrange(
            UserRole.Admin, targetIsPrimaryOwner: targetIsPrimaryOwner,
            targetRole: targetIsPrimaryOwner ? UserRole.Viewer : UserRole.Owner);
        var originalRole = access.Role;
        var originalStamp = board.UpdatedAt;

        var result = await MutateAsync(board, access, actor, revoke);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        access.Role.Should().Be(originalRole);
        board.UpdatedAt.Should().Be(originalStamp);
        _accesses.Verify(r => r.DeleteAsync(It.IsAny<BoardAccess>(), default), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(default), Times.Never);
        _unitOfWork.Verify(u => u.CommitTransactionAsync(default), Times.Never);
        _notifications.Verify(n => n.PublishAsync(It.IsAny<CreateNotificationRequestDto>(), default), Times.Never);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task EffectiveOwner_CanManageOwnerAccess(bool revoke, bool actorIsPrimaryOwner)
    {
        var (board, actor, access) = Arrange(UserRole.Owner, actorIsPrimaryOwner: actorIsPrimaryOwner);

        var result = await MutateAsync(board, access, actor, revoke);

        result.IsSuccess.Should().BeTrue();
        _unitOfWork.Verify(u => u.SaveChangesAsync(default), Times.Once);
        _accesses.Verify(r => r.DeleteAsync(access, default), revoke ? Times.Once() : Times.Never());
        if (!revoke) access.Role.Should().Be(UserRole.Editor);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Admin_CanStillManageNonOwnerAccess(bool revoke)
    {
        var (board, actor, access) = Arrange(UserRole.Admin, targetRole: UserRole.Admin);

        var result = await MutateAsync(board, access, actor, revoke);

        result.IsSuccess.Should().BeTrue();
        _unitOfWork.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Theory]
    [InlineData(UserRole.Owner)]
    [InlineData(UserRole.Admin)]
    public async Task ExistingManagerSelfRevoke_RemainsAvailable(UserRole actorRole)
    {
        var (board, actor, access) = Arrange(actorRole, targetIsActor: true, targetRole: actorRole);

        var result = await _service.RevokeAccessAsync(board.Id, access.Id, actor.Id);

        result.IsSuccess.Should().BeTrue();
        _accesses.Verify(r => r.DeleteAsync(access, default), Times.Once);
        _unitOfWork.Verify(u => u.CommitTransactionAsync(default), Times.Once);
    }

    private async Task<Result> MutateAsync(Board board, BoardAccess access, User actor, bool revoke)
    {
        if (revoke) return await _service.RevokeAccessAsync(board.Id, access.Id, actor.Id);
        return await _service.UpdateAccessAsync(board.Id, access.Id, new UpdateAccessDto(UserRole.Editor), actor.Id);
    }

    private (Board Board, User Actor, BoardAccess Access) Arrange(
        UserRole actorRole, bool actorIsPrimaryOwner = false, bool targetIsPrimaryOwner = false,
        bool targetIsActor = false, UserRole targetRole = UserRole.Owner)
    {
        var actor = new User("owner-protection-actor", "owner-protection@example.com", "unused-test-hash");
        var primaryOwnerId = actorIsPrimaryOwner ? actor.Id : Guid.NewGuid();
        var board = new Board("Owner protection", ownerId: primaryOwnerId);
        var targetUserId = targetIsActor ? actor.Id : targetIsPrimaryOwner ? primaryOwnerId : Guid.NewGuid();
        var access = new BoardAccess(board.Id, targetUserId, targetRole, primaryOwnerId);
        var actorAccess = new BoardAccess(board.Id, actor.Id, actorRole, primaryOwnerId);
        _boards.Setup(r => r.GetByIdAsync(board.Id, default)).ReturnsAsync(board);
        _users.Setup(r => r.GetByIdAsync(actor.Id, default)).ReturnsAsync(actor);
        _accesses.Setup(r => r.GetByIdAsync(access.Id, default)).ReturnsAsync(access);
        _accesses.Setup(r => r.GetByBoardAndUserAsync(board.Id, actor.Id, default)).ReturnsAsync(actorAccess);
        return (board, actor, access);
    }
}
