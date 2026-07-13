using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Moq;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Controllers;
using Taskdeck.Api.RateLimiting;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;
using Microsoft.AspNetCore.Mvc;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Tests for the POST /auth/refresh endpoint (#933).
/// Covers happy path, authentication requirements, inactive user rejection,
/// and rate limiting.
/// </summary>
public class AuthRefreshEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly JwtSettings DefaultJwtSettings = new()
    {
        SecretKey = "TaskdeckTestsOnlySecretKeyMustBeLongEnough123!",
        Issuer = "TaskdeckTests",
        Audience = "TaskdeckUsers",
        ExpirationMinutes = 60
    };

    private readonly TestWebApplicationFactory _factory;

    public AuthRefreshEndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ─────────────────────────────────────────────────────────
    // Integration tests (full HTTP pipeline)
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_ShouldReturnNewToken_WhenAuthenticated()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "refresh-happy");

        var response = await client.PostAsync("/api/auth/refresh", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AuthResultDto>();
        result.Should().NotBeNull();
        result!.Token.Should().NotBeNullOrWhiteSpace();
        result.Token.Should().NotBe(user.Token, "refresh should issue a new token, not return the same one");
        result.User.Should().NotBeNull();
        result.User.Id.Should().Be(user.UserId);
        result.User.Username.Should().Be(user.Username);
    }

    [Fact]
    public async Task Refresh_ShouldReturn401_WhenNotAuthenticated()
    {
        using var client = _factory.CreateClient();
        // No authentication header set

        var response = await client.PostAsync("/api/auth/refresh", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_ShouldReturnSameUserProfile_AsOriginalLogin()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "refresh-profile");

        var response = await client.PostAsync("/api/auth/refresh", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AuthResultDto>();
        result.Should().NotBeNull();
        result!.User.Id.Should().Be(user.UserId);
        result.User.Email.Should().Be(user.Email);
        result.User.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Refresh_NewTokenShouldBeUsableForAuthenticatedRequests()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "refresh-usable");

        var refreshResponse = await client.PostAsync("/api/auth/refresh", null);
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await refreshResponse.Content.ReadFromJsonAsync<AuthResultDto>();
        result.Should().NotBeNull();

        // Use the new token for a subsequent authenticated request
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result!.Token);

        // Try accessing an authenticated endpoint with the new token
        var boardsResponse = await client.GetAsync("/api/boards");
        boardsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Refresh_ShouldThrottlePerUser_WhenRateLimited()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimiting:Enabled", "true");
            builder.UseSetting("RateLimiting:AuthPerIp:PermitLimit", "200");
            builder.UseSetting("RateLimiting:AuthPerIp:WindowSeconds", "60");
            builder.UseSetting("RateLimiting:TokenRefreshPerUser:PermitLimit", "1");
            builder.UseSetting("RateLimiting:TokenRefreshPerUser:WindowSeconds", "60");
        });
        using var client = factory.CreateClient();

        await ApiTestHarness.AuthenticateAsync(client, "refresh-rate");

        // First refresh should succeed
        var first = await client.PostAsync("/api/auth/refresh", null);
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        // Update token from first refresh so second request is still authenticated
        var firstResult = await first.Content.ReadFromJsonAsync<AuthResultDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", firstResult!.Token);

        // Second refresh should be rate-limited
        var second = await client.PostAsync("/api/auth/refresh", null);
        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        second.Headers.TryGetValues("X-RateLimit-Policy", out var policyValues).Should().BeTrue();
        policyValues.Should().ContainSingle().Which.Should().Be(RateLimitingPolicyNames.TokenRefreshPerUser);
    }

    [Fact]
    public async Task Refresh_ShouldNotThrottleDifferentUsers()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimiting:Enabled", "true");
            builder.UseSetting("RateLimiting:AuthPerIp:PermitLimit", "200");
            builder.UseSetting("RateLimiting:AuthPerIp:WindowSeconds", "60");
            builder.UseSetting("RateLimiting:TokenRefreshPerUser:PermitLimit", "1");
            builder.UseSetting("RateLimiting:TokenRefreshPerUser:WindowSeconds", "60");
        });
        using var client1 = factory.CreateClient();
        using var client2 = factory.CreateClient();

        await ApiTestHarness.AuthenticateAsync(client1, "refresh-user-a");
        await ApiTestHarness.AuthenticateAsync(client2, "refresh-user-b");

        // User A refreshes (consumes their 1 permit)
        var response1 = await client1.PostAsync("/api/auth/refresh", null);
        response1.StatusCode.Should().Be(HttpStatusCode.OK);

        // User B should still be able to refresh (different rate limit partition)
        var response2 = await client2.PostAsync("/api/auth/refresh", null);
        response2.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ─────────────────────────────────────────────────────────
    // Unit tests (controller-level, mocked dependencies)
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RefreshToken_ShouldReturn401_WhenUserContextNotAuthenticated()
    {
        var (uow, authService) = CreateMockAuthServiceWithUow();
        var userContext = CreateMockUserContext(authenticated: false);
        var controller = CreateController(authService, userContext.Object, uow.Object);

        var result = await controller.RefreshToken();

        var unauthorized = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
        var error = unauthorized.Value.Should().BeOfType<ApiErrorResponse>().Subject;
        error.ErrorCode.Should().Be(ErrorCodes.AuthenticationFailed);
    }

    [Fact]
    public async Task RefreshToken_ShouldReturnNewToken_WhenUserIsActive()
    {
        var userId = Guid.NewGuid();
        var (uow, authService) = CreateMockAuthServiceWithUow();
        var userContext = CreateMockUserContext(authenticated: true, userId: userId);

        var testUser = new User("testuser", "test@test.com", BCrypt.Net.BCrypt.HashPassword("pass"));
        SetUserId(testUser, userId);

        var userRepoMock = new Mock<IUserRepository>();
        userRepoMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(testUser);
        uow.Setup(u => u.Users).Returns(userRepoMock.Object);

        var controller = CreateController(authService, userContext.Object, uow.Object);

        var result = await controller.RefreshToken();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var authResult = ok.Value.Should().BeOfType<AuthResultDto>().Subject;
        authResult.Token.Should().NotBeNullOrWhiteSpace();
        authResult.User.Id.Should().Be(userId);
        authResult.User.Username.Should().Be("testuser");
        authResult.User.Email.Should().Be("test@test.com");
        authResult.User.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task RefreshToken_ShouldReturn403_WhenUserIsInactive()
    {
        var userId = Guid.NewGuid();
        var (uow, authService) = CreateMockAuthServiceWithUow();
        var userContext = CreateMockUserContext(authenticated: true, userId: userId);

        var testUser = new User("inactiveuser", "inactive@test.com", BCrypt.Net.BCrypt.HashPassword("pass"));
        SetUserId(testUser, userId);
        testUser.Deactivate();

        var userRepoMock = new Mock<IUserRepository>();
        userRepoMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(testUser);
        uow.Setup(u => u.Users).Returns(userRepoMock.Object);

        var controller = CreateController(authService, userContext.Object, uow.Object);

        var result = await controller.RefreshToken();

        // The service returns Forbidden for inactive users, which maps to 403
        result.Should().BeAssignableTo<ObjectResult>();
        var objectResult = (ObjectResult)result;
        objectResult.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task RefreshToken_ShouldReturn401_WhenUserNotFound()
    {
        var userId = Guid.NewGuid();
        var (uow, authService) = CreateMockAuthServiceWithUow();
        var userContext = CreateMockUserContext(authenticated: true, userId: userId);

        var userRepoMock = new Mock<IUserRepository>();
        userRepoMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        uow.Setup(u => u.Users).Returns(userRepoMock.Object);

        var controller = CreateController(authService, userContext.Object, uow.Object);

        var result = await controller.RefreshToken();

        var unauthorized = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
        var error = unauthorized.Value.Should().BeOfType<ApiErrorResponse>().Subject;
        error.ErrorCode.Should().Be(ErrorCodes.Unauthorized);
    }

    // ─────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────

    private static AuthController CreateController(
        AuthenticationService authService,
        IUserContext userContext,
        IUnitOfWork unitOfWork)
    {
        return new AuthController(
            authService,
            new GitHubOAuthSettings(),
            new OidcSettings(),
            CreateMockMfaService(),
            userContext,
            unitOfWork);
    }

    private static (Mock<IUnitOfWork> UnitOfWork, AuthenticationService AuthService) CreateMockAuthServiceWithUow()
    {
        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var userRepoMock = new Mock<IUserRepository>();
        unitOfWorkMock.Setup(u => u.Users).Returns(userRepoMock.Object);
        unitOfWorkMock.Setup(u => u.ExternalLogins).Returns(new Mock<IExternalLoginRepository>().Object);

        var authService = new AuthenticationService(
            unitOfWorkMock.Object,
            DefaultJwtSettings,
            Mock.Of<IRegistrationPolicyService>());
        return (unitOfWorkMock, authService);
    }

    private static Mock<IUserContext> CreateMockUserContext(bool authenticated = true, Guid? userId = null)
    {
        var mock = new Mock<IUserContext>();
        mock.Setup(u => u.IsAuthenticated).Returns(authenticated);
        mock.Setup(u => u.UserId).Returns(authenticated ? (userId ?? Guid.NewGuid()).ToString() : null!);
        return mock;
    }

    private static MfaService CreateMockMfaService()
    {
        var unitOfWorkMock = new Mock<IUnitOfWork>();
        unitOfWorkMock.Setup(u => u.Users).Returns(new Mock<IUserRepository>().Object);
        unitOfWorkMock.Setup(u => u.MfaCredentials).Returns(new Mock<IMfaCredentialRepository>().Object);
        var policySettings = new MfaPolicySettings();
        return new MfaService(unitOfWorkMock.Object, policySettings);
    }

    private static void SetUserId(User user, Guid userId)
    {
        var idProperty = typeof(Domain.Common.Entity).GetProperty("Id");
        idProperty!.SetValue(user, userId);
    }
}
