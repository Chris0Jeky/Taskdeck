using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

/// <summary>
/// Security-focused tests for OIDC external login flows.
/// Validates claim mapping, email collision protection, and failure modes.
/// </summary>
public class OidcSecurityTests
{
    private static readonly JwtSettings DefaultJwtSettings = new()
    {
        SecretKey = "ThisIsATestSecretKeyThatIsAtLeast32CharactersLong!",
        Issuer = "TestIssuer",
        Audience = "TestAudience",
        ExpirationMinutes = 60
    };

    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IUserRepository> _userRepoMock;
    private readonly Mock<IExternalLoginRepository> _externalLoginRepoMock;

    public OidcSecurityTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _userRepoMock = new Mock<IUserRepository>();
        _externalLoginRepoMock = new Mock<IExternalLoginRepository>();

        _unitOfWorkMock.Setup(u => u.Users).Returns(_userRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.ExternalLogins).Returns(_externalLoginRepoMock.Object);
    }

    private AuthenticationService CreateService() => new(_unitOfWorkMock.Object, DefaultJwtSettings);

    // ── Provider Validation ─────────────────────────────────────────

    [Fact]
    public async Task ExternalLogin_ShouldReject_EmptyProvider()
    {
        var service = CreateService();
        var dto = new ExternalLoginDto("", "user123", "testuser", "test@example.com");

        var result = await service.ExternalLoginAsync(dto);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task ExternalLogin_ShouldReject_EmptyProviderUserId()
    {
        var service = CreateService();
        var dto = new ExternalLoginDto("oidc_entra", "", "testuser", "test@example.com");

        var result = await service.ExternalLoginAsync(dto);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    // ── Email Collision Protection ──────────────────────────────────

    [Fact]
    public async Task ExternalLogin_ShouldNotAutoLink_WhenEmailCollides()
    {
        var service = CreateService();
        var existingUser = new User("existing", "shared@example.com", BCrypt.Net.BCrypt.HashPassword("password"));
        _userRepoMock.Setup(r => r.GetByEmailAsync("shared@example.com", default)).ReturnsAsync(existingUser);
        _externalLoginRepoMock.Setup(r => r.GetByProviderAsync("oidc_entra", "attacker123", default)).ReturnsAsync((ExternalLogin?)null);

        var dto = new ExternalLoginDto("oidc_entra", "attacker123", "attacker", "shared@example.com");

        var result = await service.ExternalLoginAsync(dto);

        // Should succeed but create a NEW user, not link to the existing one
        result.IsSuccess.Should().BeTrue();
        result.Value.User.Id.Should().NotBe(existingUser.Id);
    }

    [Fact]
    public async Task ExternalLogin_ShouldGenerateUniqueEmail_WhenCollision()
    {
        var service = CreateService();
        var existingUser = new User("existing", "shared@example.com", BCrypt.Net.BCrypt.HashPassword("password"));
        _userRepoMock.Setup(r => r.GetByEmailAsync("shared@example.com", default)).ReturnsAsync(existingUser);
        _externalLoginRepoMock.Setup(r => r.GetByProviderAsync("oidc_entra", "user456", default)).ReturnsAsync((ExternalLogin?)null);

        var dto = new ExternalLoginDto("oidc_entra", "user456", "newuser", "shared@example.com");

        var result = await service.ExternalLoginAsync(dto);

        result.IsSuccess.Should().BeTrue();
        // The email used should NOT be the colliding one
        result.Value.User.Email.Should().NotBe("shared@example.com");
        result.Value.User.Email.Should().Contain("external.taskdeck.local");
    }

    // ── Existing Provider Link ──────────────────────────────────────

    [Fact]
    public async Task ExternalLogin_ShouldReturnExistingUser_WhenProviderLinkExists()
    {
        var service = CreateService();
        var existingUser = new User("linked-user", "linked@example.com", BCrypt.Net.BCrypt.HashPassword("password"));
        var externalLogin = new ExternalLogin(existingUser.Id, "oidc_google", "google123", "Test User");

        _externalLoginRepoMock.Setup(r => r.GetByProviderAsync("oidc_google", "google123", default)).ReturnsAsync(externalLogin);
        _userRepoMock.Setup(r => r.GetByIdAsync(existingUser.Id, default)).ReturnsAsync(existingUser);

        var dto = new ExternalLoginDto("oidc_google", "google123", "linked-user", "linked@example.com");

        var result = await service.ExternalLoginAsync(dto);

        result.IsSuccess.Should().BeTrue();
        result.Value.User.Id.Should().Be(existingUser.Id);
    }

    [Fact]
    public async Task ExternalLogin_ShouldReject_InactiveLinkedUser()
    {
        var service = CreateService();
        var inactiveUser = new User("inactive-user", "inactive@example.com", BCrypt.Net.BCrypt.HashPassword("password"));
        inactiveUser.Deactivate();
        var externalLogin = new ExternalLogin(inactiveUser.Id, "oidc_google", "google789");

        _externalLoginRepoMock.Setup(r => r.GetByProviderAsync("oidc_google", "google789", default)).ReturnsAsync(externalLogin);
        _userRepoMock.Setup(r => r.GetByIdAsync(inactiveUser.Id, default)).ReturnsAsync(inactiveUser);

        var dto = new ExternalLoginDto("oidc_google", "google789", "inactive-user", "inactive@example.com");

        var result = await service.ExternalLoginAsync(dto);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
    }

    // ── Username Collision Handling ──────────────────────────────────

    [Fact]
    public async Task ExternalLogin_ShouldDeduplicateUsername_WhenCollision()
    {
        var service = CreateService();
        var existingUser = new User("oidcuser", "other@example.com", BCrypt.Net.BCrypt.HashPassword("password"));
        _userRepoMock.Setup(r => r.GetByUsernameAsync("oidcuser", default)).ReturnsAsync(existingUser);
        _userRepoMock.Setup(r => r.GetByUsernameAsync("oidcuser1", default)).ReturnsAsync((User?)null);
        _externalLoginRepoMock.Setup(r => r.GetByProviderAsync("oidc_entra", "entra999", default)).ReturnsAsync((ExternalLogin?)null);

        var dto = new ExternalLoginDto("oidc_entra", "entra999", "oidcuser", "oidcuser@example.com");

        var result = await service.ExternalLoginAsync(dto);

        result.IsSuccess.Should().BeTrue();
        result.Value.User.Username.Should().Be("oidcuser1");
    }

    // ── OIDC Provider Config Tests ──────────────────────────────────

    [Fact]
    public void OidcProviderConfig_IsConfigured_ShouldBeFalse_WhenMissingFields()
    {
        var config = new OidcProviderConfig
        {
            Name = "test",
            Authority = "https://login.example.com",
            ClientId = "",
            ClientSecret = "secret"
        };

        config.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void OidcProviderConfig_IsConfigured_ShouldBeTrue_WhenAllFieldsSet()
    {
        var config = new OidcProviderConfig
        {
            Name = "entra",
            Authority = "https://login.microsoftonline.com/tenant",
            ClientId = "client-id",
            ClientSecret = "client-secret"
        };

        config.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void OidcSettings_ConfiguredProviders_ShouldFilterIncomplete()
    {
        var settings = new OidcSettings
        {
            Providers =
            [
                new OidcProviderConfig
                {
                    Name = "complete",
                    Authority = "https://auth.example.com",
                    ClientId = "id",
                    ClientSecret = "secret"
                },
                new OidcProviderConfig
                {
                    Name = "incomplete",
                    Authority = "",
                    ClientId = "id",
                    ClientSecret = "secret"
                }
            ]
        };

        settings.ConfiguredProviders.Should().HaveCount(1);
        settings.ConfiguredProviders[0].Name.Should().Be("complete");
    }

    // ── Cross-Provider Identity Isolation ────────────────────────────

    [Fact]
    public async Task ExternalLogin_ShouldNotLinkAcrossProviders()
    {
        var service = CreateService();

        // User exists via GitHub but login comes from OIDC Entra with same provider user ID
        _externalLoginRepoMock.Setup(r => r.GetByProviderAsync("oidc_entra", "shared-id", default)).ReturnsAsync((ExternalLogin?)null);
        _externalLoginRepoMock.Setup(r => r.GetByProviderAsync("GitHub", "shared-id", default)).ReturnsAsync(
            new ExternalLogin(Guid.NewGuid(), "GitHub", "shared-id"));

        var dto = new ExternalLoginDto("oidc_entra", "shared-id", "crossuser", "cross@example.com");

        var result = await service.ExternalLoginAsync(dto);

        // Should create a new user, not link to GitHub user
        result.IsSuccess.Should().BeTrue();
    }
}
