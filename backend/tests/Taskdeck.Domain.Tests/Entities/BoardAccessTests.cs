using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class BoardAccessTests
{
    [Fact]
    public void Constructor_ShouldCreateBoardAccess_WithValidData()
    {
        // Arrange
        var boardId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var grantedBy = Guid.NewGuid();

        // Act
        var access = new BoardAccess(boardId, userId, UserRole.Editor, grantedBy);

        // Assert
        access.BoardId.Should().Be(boardId);
        access.UserId.Should().Be(userId);
        access.Role.Should().Be(UserRole.Editor);
        access.GrantedBy.Should().Be(grantedBy);
        access.CanRead().Should().BeTrue();
        access.CanWrite().Should().BeTrue();
        access.CanManageAccess().Should().BeFalse();
        access.CanDelete().Should().BeFalse();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenRoleIsInvalid()
    {
        // Act
        var act = () => new BoardAccess(Guid.NewGuid(), Guid.NewGuid(), (UserRole)999, Guid.NewGuid());

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Role value is invalid")
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void UpdateRole_ShouldThrow_WhenRoleIsInvalid()
    {
        // Arrange
        var access = new BoardAccess(Guid.NewGuid(), Guid.NewGuid(), UserRole.Editor, Guid.NewGuid());

        // Act
        var act = () => access.UpdateRole((UserRole)999, Guid.NewGuid());

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Role value is invalid")
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void UpdateRole_ShouldUpdateRoleAndGrantMetadata()
    {
        // Arrange
        var access = new BoardAccess(Guid.NewGuid(), Guid.NewGuid(), UserRole.Viewer, Guid.NewGuid());
        var updatedBy = Guid.NewGuid();

        // Act
        access.UpdateRole(UserRole.Admin, updatedBy);

        // Assert
        access.Role.Should().Be(UserRole.Admin);
        access.GrantedBy.Should().Be(updatedBy);
        access.CanManageAccess().Should().BeTrue();
    }
}
