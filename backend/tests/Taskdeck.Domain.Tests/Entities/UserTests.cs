using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class UserTests
{
    [Fact]
    public void Constructor_ShouldCreateUser_WithValidData()
    {
        // Arrange & Act
        var user = new User("john.doe", "John.Doe@example.com", "hashed-password", UserRole.Admin);

        // Assert
        user.Username.Should().Be("john.doe");
        user.Email.Should().Be("john.doe@example.com");
        user.PasswordHash.Should().Be("hashed-password");
        user.DefaultRole.Should().Be(UserRole.Admin);
        user.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenDefaultRoleIsInvalid()
    {
        // Act
        var act = () => new User("john.doe", "john@example.com", "hashed-password", (UserRole)999);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Default role value is invalid")
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenEmailIsInvalid()
    {
        // Act
        var act = () => new User("john.doe", "invalid-email", "hashed-password");

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Email must be valid")
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void UpdateDefaultRole_ShouldThrow_WhenRoleIsInvalid()
    {
        // Arrange
        var user = new User("john.doe", "john@example.com", "hashed-password");

        // Act
        var act = () => user.UpdateDefaultRole((UserRole)999);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Default role value is invalid")
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void UpdatePassword_ShouldUpdateHash()
    {
        // Arrange
        var user = new User("john.doe", "john@example.com", "old-hash");

        // Act
        user.UpdatePassword("new-hash");

        // Assert
        user.PasswordHash.Should().Be("new-hash");
    }

    [Fact]
    public void MfaEnabled_ShouldDefaultToFalse()
    {
        var user = new User("testuser", "test@example.com", "hash123");
        user.MfaEnabled.Should().BeFalse();
    }

    [Fact]
    public void EnableMfa_ShouldSetMfaEnabledToTrue()
    {
        var user = new User("testuser", "test@example.com", "hash123");
        user.EnableMfa();
        user.MfaEnabled.Should().BeTrue();
    }

    [Fact]
    public void DisableMfa_ShouldSetMfaEnabledToFalse()
    {
        var user = new User("testuser", "test@example.com", "hash123");
        user.EnableMfa();
        user.DisableMfa();
        user.MfaEnabled.Should().BeFalse();
    }
}
