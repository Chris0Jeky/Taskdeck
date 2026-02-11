using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class AuditLogTests
{
    [Fact]
    public void Constructor_ShouldCreateAuditLog_WithValidData()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        // Act
        var auditLog = new AuditLog("Card", entityId, AuditAction.Updated, userId, "{\"title\":\"new\"}");

        // Assert
        auditLog.EntityType.Should().Be("Card");
        auditLog.EntityId.Should().Be(entityId);
        auditLog.Action.Should().Be(AuditAction.Updated);
        auditLog.UserId.Should().Be(userId);
        auditLog.Changes.Should().Be("{\"title\":\"new\"}");
        auditLog.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenEntityTypeIsEmpty()
    {
        // Act
        var act = () => new AuditLog(string.Empty, Guid.NewGuid(), AuditAction.Created);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Entity type cannot be empty")
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenUserIdIsEmpty()
    {
        // Act
        var act = () => new AuditLog("Board", Guid.NewGuid(), AuditAction.Created, Guid.Empty);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("User ID cannot be empty")
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }
}
