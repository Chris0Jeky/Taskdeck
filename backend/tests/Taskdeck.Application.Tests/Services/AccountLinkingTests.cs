using FluentAssertions;
using Moq;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

/// <summary>
/// Tests for account linking and unlinking functionality in AuthenticationService.
/// Linked to #676.
/// </summary>
public class AccountLinkingTests
{
    private static readonly JwtSettings DefaultJwtSettings = new()
    {
        SecretKey = "TestKeyMustBeAtLeast32CharactersLong!",
        Issuer = "TestIssuer",
        Audience = "TestAudience",
        ExpirationMinutes = 60
    };

    [Fact]
    public async Task CompleteAccountLinkAsync_ValidNewLink_CreatesExternalLogin()
    {
        var userId = Guid.NewGuid();
        var user = new User("testuser", "test@example.com", BCrypt.Net.BCrypt.HashPassword("pass"));
        SetUserId(user, userId);

        var (service, uow) = CreateService();
        uow.Setup(u => u.Users.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        uow.Setup(u => u.ExternalLogins.GetByProviderAsync("GitHub", "gh-123", It.IsAny<CancellationToken>())).ReturnsAsync((ExternalLogin?)null);
        uow.Setup(u => u.ExternalLogins.GetByUserIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(Enumerable.Empty<ExternalLogin>());
        uow.Setup(u => u.ExternalLogins.AddAsync(It.IsAny<ExternalLogin>(), It.IsAny<CancellationToken>())).ReturnsAsync((ExternalLogin e, CancellationToken _) => e);

        var result = await service.CompleteAccountLinkAsync(userId, "GitHub", "gh-123", "Test User", "https://avatar.url/img.png");

        result.IsSuccess.Should().BeTrue();
        result.Value.Provider.Should().Be("GitHub");
        result.Value.ProviderUserId.Should().Be("gh-123");
        result.Value.DisplayName.Should().Be("Test User");
        uow.Verify(u => u.ExternalLogins.AddAsync(It.IsAny<ExternalLogin>(), It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CompleteAccountLinkAsync_AlreadyLinkedToSameUser_ReturnsConflict()
    {
        var userId = Guid.NewGuid();
        var user = new User("testuser", "test@example.com", BCrypt.Net.BCrypt.HashPassword("pass"));
        SetUserId(user, userId);

        var existingLogin = new ExternalLogin(userId, "GitHub", "gh-123");

        var (service, uow) = CreateService();
        uow.Setup(u => u.Users.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        uow.Setup(u => u.ExternalLogins.GetByProviderAsync("GitHub", "gh-123", It.IsAny<CancellationToken>())).ReturnsAsync(existingLogin);

        var result = await service.CompleteAccountLinkAsync(userId, "GitHub", "gh-123", null, null);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("already linked to your account");
    }

    [Fact]
    public async Task CompleteAccountLinkAsync_AlreadyLinkedToDifferentUser_ReturnsConflict()
    {
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var user = new User("testuser", "test@example.com", BCrypt.Net.BCrypt.HashPassword("pass"));
        SetUserId(user, userId);

        var existingLogin = new ExternalLogin(otherUserId, "GitHub", "gh-123");

        var (service, uow) = CreateService();
        uow.Setup(u => u.Users.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        uow.Setup(u => u.ExternalLogins.GetByProviderAsync("GitHub", "gh-123", It.IsAny<CancellationToken>())).ReturnsAsync(existingLogin);

        var result = await service.CompleteAccountLinkAsync(userId, "GitHub", "gh-123", null, null);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("different user");
    }

    [Fact]
    public async Task CompleteAccountLinkAsync_UserAlreadyHasProviderLinked_ReturnsConflict()
    {
        var userId = Guid.NewGuid();
        var user = new User("testuser", "test@example.com", BCrypt.Net.BCrypt.HashPassword("pass"));
        SetUserId(user, userId);

        var existingUserLogin = new ExternalLogin(userId, "GitHub", "gh-existing");

        var (service, uow) = CreateService();
        uow.Setup(u => u.Users.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        uow.Setup(u => u.ExternalLogins.GetByProviderAsync("GitHub", "gh-new", It.IsAny<CancellationToken>())).ReturnsAsync((ExternalLogin?)null);
        uow.Setup(u => u.ExternalLogins.GetByUserIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { existingUserLogin });

        var result = await service.CompleteAccountLinkAsync(userId, "GitHub", "gh-new", null, null);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("already linked");
    }

    [Fact]
    public async Task CompleteAccountLinkAsync_UserNotFound_ReturnsNotFound()
    {
        var (service, uow) = CreateService();
        uow.Setup(u => u.Users.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var result = await service.CompleteAccountLinkAsync(Guid.NewGuid(), "GitHub", "gh-123", null, null);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task CompleteAccountLinkAsync_InactiveUser_ReturnsForbidden()
    {
        var userId = Guid.NewGuid();
        var user = new User("testuser", "test@example.com", BCrypt.Net.BCrypt.HashPassword("pass"));
        SetUserId(user, userId);
        user.Deactivate();

        var (service, uow) = CreateService();
        uow.Setup(u => u.Users.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var result = await service.CompleteAccountLinkAsync(userId, "GitHub", "gh-123", null, null);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task UnlinkExternalLoginAsync_ExistingLink_RemovesIt()
    {
        var userId = Guid.NewGuid();
        var user = new User("testuser", "test@example.com", BCrypt.Net.BCrypt.HashPassword("pass"));
        SetUserId(user, userId);

        var login = new ExternalLogin(userId, "GitHub", "gh-123");

        var (service, uow) = CreateService();
        uow.Setup(u => u.Users.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        uow.Setup(u => u.ExternalLogins.GetByUserIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { login });

        var result = await service.UnlinkExternalLoginAsync(userId, "GitHub");

        result.IsSuccess.Should().BeTrue();
        uow.Verify(u => u.ExternalLogins.DeleteAsync(login, It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnlinkExternalLoginAsync_NoExistingLink_ReturnsNotFound()
    {
        var userId = Guid.NewGuid();
        var user = new User("testuser", "test@example.com", BCrypt.Net.BCrypt.HashPassword("pass"));
        SetUserId(user, userId);

        var (service, uow) = CreateService();
        uow.Setup(u => u.Users.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        uow.Setup(u => u.ExternalLogins.GetByUserIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(Enumerable.Empty<ExternalLogin>());

        var result = await service.UnlinkExternalLoginAsync(userId, "GitHub");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task UnlinkExternalLoginAsync_UserNotFound_ReturnsNotFound()
    {
        var (service, uow) = CreateService();
        uow.Setup(u => u.Users.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var result = await service.UnlinkExternalLoginAsync(Guid.NewGuid(), "GitHub");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    // ─────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────

    private static (AuthenticationService Service, Mock<IUnitOfWork> UnitOfWork) CreateService()
    {
        var uow = new Mock<IUnitOfWork>();
        var service = new AuthenticationService(uow.Object, DefaultJwtSettings);
        return (service, uow);
    }

    private static void SetUserId(User user, Guid userId)
    {
        var idProperty = typeof(Domain.Common.Entity).GetProperty("Id");
        idProperty!.SetValue(user, userId);
    }
}
