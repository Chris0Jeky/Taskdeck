using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Controllers;
using Taskdeck.Api.Middleware;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Edge-case integration tests for AuthController and TokenValidationMiddleware,
/// verifying security properties around OAuth flows, JWT lifecycle,
/// and session invalidation.
/// Linked to #707 (TST-40).
/// </summary>
public class AuthControllerEdgeCaseTests
{
    private static readonly JwtSettings DefaultJwtSettings = new()
    {
        SecretKey = "TaskdeckTestsOnlySecretKeyMustBeLongEnough123!",
        Issuer = "TaskdeckTests",
        Audience = "TaskdeckUsers",
        ExpirationMinutes = 60
    };

    // ─────────────────────────────────────────────────────────
    // OAuth code exchange edge cases
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ExchangeCode_ShouldReturn400_WhenCodeIsEmpty()
    {
        var (controller, _) = CreateAuthControllerWithUnitOfWork();
        var result = await controller.ExchangeCode(new ExchangeCodeRequest(string.Empty));

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<ApiErrorResponse>().Subject;
        error.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task ExchangeCode_ShouldReturn401_WhenCodeIsInvalid()
    {
        var (controller, _) = CreateAuthControllerWithUnitOfWork();
        var result = await controller.ExchangeCode(new ExchangeCodeRequest("nonexistent-code"));

        var unauthorized = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
        var error = unauthorized.Value.Should().BeOfType<ApiErrorResponse>().Subject;
        error.ErrorCode.Should().Be(ErrorCodes.AuthenticationFailed);
    }

    [Fact]
    public async Task ExchangeCode_ShouldPreventReplay_SecondUseOfSameCode()
    {
        var (controller, uow) = CreateAuthControllerWithUnitOfWork();

        // Create a valid auth code
        var code = "test-replay-code";
        var userId = Guid.NewGuid();
        var authCode = new OAuthAuthCode(code, userId, "fake-token", DateTimeOffset.UtcNow.AddSeconds(60));

        var authCodeRepoMock = new Mock<IOAuthAuthCodeRepository>();
        authCodeRepoMock.Setup(r => r.GetByCodeAsync(code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(authCode);

        // First TryConsumeAtomicAsync returns true (consumed), second returns false (already consumed)
        var consumeCallCount = 0;
        authCodeRepoMock.Setup(r => r.TryConsumeAtomicAsync(code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                consumeCallCount++;
                return consumeCallCount == 1;
            });

        var userRepoMock = new Mock<IUserRepository>();
        var testUser = new User("testuser", "test@test.com", BCrypt.Net.BCrypt.HashPassword("pass"));
        SetUserId(testUser, userId);
        userRepoMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(testUser);

        uow.Setup(u => u.OAuthAuthCodes).Returns(authCodeRepoMock.Object);
        uow.Setup(u => u.Users).Returns(userRepoMock.Object);

        // First exchange — success
        var first = await controller.ExchangeCode(new ExchangeCodeRequest(code));
        first.Should().BeOfType<OkObjectResult>();

        // Second exchange with same code — should fail (atomic consume returns false)
        var second = await controller.ExchangeCode(new ExchangeCodeRequest(code));
        second.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task ExchangeCode_ShouldReturn401_WhenCodeHasExpired()
    {
        var (controller, uow) = CreateAuthControllerWithUnitOfWork();

        // Create a code that is already expired by manipulating the entity via reflection
        var code = "test-expired-code";
        var authCode = CreateExpiredAuthCode(code);

        var authCodeRepoMock = new Mock<IOAuthAuthCodeRepository>();
        authCodeRepoMock.Setup(r => r.GetByCodeAsync(code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(authCode);
        uow.Setup(u => u.OAuthAuthCodes).Returns(authCodeRepoMock.Object);

        var result = await controller.ExchangeCode(new ExchangeCodeRequest(code));

        var unauthorized = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
        var error = unauthorized.Value.Should().BeOfType<ApiErrorResponse>().Subject;
        error.Message.Should().Contain("expired");
    }

    [Fact]
    public void GetProviders_ShouldReturnGitHubStatus()
    {
        var (controller, _) = CreateAuthControllerWithUnitOfWork(gitHubConfigured: true);
        var result = controller.GetProviders();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().NotBeNull();
    }

    // ─────────────────────────────────────────────────────────
    // GitHub login edge cases
    // ─────────────────────────────────────────────────────────

    [Fact]
    public void GitHubLogin_ShouldReturn404_WhenNotConfigured()
    {
        var (controller, _) = CreateAuthControllerWithUnitOfWork(gitHubConfigured: false);
        var result = controller.GitHubLogin();

        var notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        var error = notFound.Value.Should().BeOfType<ApiErrorResponse>().Subject;
        error.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public void GitHubLogin_ShouldReturn400_WhenReturnUrlIsExternal()
    {
        var (controller, _) = CreateAuthControllerWithUnitOfWork(gitHubConfigured: true);

        var urlHelper = new Mock<IUrlHelper>();
        urlHelper.Setup(u => u.IsLocalUrl("https://evil.com/steal")).Returns(false);
        controller.Url = urlHelper.Object;

        var result = controller.GitHubLogin(returnUrl: "https://evil.com/steal");

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<ApiErrorResponse>().Subject;
        error.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        error.Message.Should().Contain("Invalid return URL");
    }

    // ─────────────────────────────────────────────────────────
    // GitHub login — flow selection must use server-side auth state
    // (regression for CodeQL CWE-807: user-controlled bypass)
    // ─────────────────────────────────────────────────────────

    [Fact]
    public void GitHubLogin_UnauthenticatedCaller_StartsNormalLoginFlow()
    {
        // Arrange — controller with an unauthenticated user context
        var (uow, authService) = CreateMockAuthServiceWithUow();
        var userContext = CreateMockUserContext(authenticated: false);
        var authCodeRepoMock = new Mock<IOAuthAuthCodeRepository>();
        authCodeRepoMock.Setup(r => r.GetByCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OAuthAuthCode?)null);
        uow.Setup(u => u.OAuthAuthCodes).Returns(authCodeRepoMock.Object);
        var controller = new AuthController(authService.Object, CreateGitHubSettings(true), userContext.Object, uow.Object);

        var urlHelper = new Mock<IUrlHelper>();
        urlHelper.Setup(u => u.IsLocalUrl(It.IsAny<string>())).Returns(true);
        urlHelper.Setup(u => u.Action(It.IsAny<Microsoft.AspNetCore.Mvc.Routing.UrlActionContext>())).Returns("/auth/callback");
        controller.Url = urlHelper.Object;

        // Act
        var result = controller.GitHubLogin();

        // Assert — a ChallengeResult is returned (not Unauthorized)
        // and the link mode items must NOT be present (flow driven by auth state, not user input)
        var challenge = result.Should().BeOfType<ChallengeResult>().Subject;
        challenge.Properties!.Items.Should().NotContainKey("mode");
        challenge.Properties.Items.Should().NotContainKey("link_user_id");
    }

    [Fact]
    public void GitHubLogin_AuthenticatedCaller_StartsLinkFlowFromServerState()
    {
        // Arrange — controller with an authenticated user context (server-side state)
        var callerId = Guid.NewGuid();
        var (uow, authService) = CreateMockAuthServiceWithUow();
        var userContext = CreateMockUserContext(authenticated: true, userId: callerId);
        var authCodeRepoMock = new Mock<IOAuthAuthCodeRepository>();
        authCodeRepoMock.Setup(r => r.GetByCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OAuthAuthCode?)null);
        uow.Setup(u => u.OAuthAuthCodes).Returns(authCodeRepoMock.Object);
        var controller = new AuthController(authService.Object, CreateGitHubSettings(true), userContext.Object, uow.Object);

        var urlHelper = new Mock<IUrlHelper>();
        urlHelper.Setup(u => u.IsLocalUrl(It.IsAny<string>())).Returns(true);
        urlHelper.Setup(u => u.Action(It.IsAny<Microsoft.AspNetCore.Mvc.Routing.UrlActionContext>())).Returns("/auth/callback");
        controller.Url = urlHelper.Object;

        // Act — no mode query parameter supplied; flow must be determined server-side
        var result = controller.GitHubLogin();

        // Assert — link mode must be set from the server-side JWT identity, not user input
        var challenge = result.Should().BeOfType<ChallengeResult>().Subject;
        challenge.Properties!.Items.Should().ContainKey("mode").WhoseValue.Should().Be("link");
        challenge.Properties.Items.Should().ContainKey("link_user_id").WhoseValue.Should().Be(callerId.ToString());
    }

    // ─────────────────────────────────────────────────────────
    // TokenValidationMiddleware — account deletion during active session
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task TokenValidationMiddleware_ShouldReturn401_WhenUserDeletedDuringSession()
    {
        var userId = Guid.NewGuid();
        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var userRepoMock = new Mock<IUserRepository>();
        unitOfWorkMock.Setup(u => u.Users).Returns(userRepoMock.Object);

        userRepoMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var middleware = new TokenValidationMiddleware(next, NullLogger<TokenValidationMiddleware>.Instance);

        var context = CreateAuthenticatedContext(userId, DateTimeOffset.UtcNow);
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context, unitOfWorkMock.Object);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);

        var body = await ReadResponseBody(context);
        body.ErrorCode.Should().Be(ErrorCodes.Unauthorized);
    }

    [Fact]
    public async Task TokenValidationMiddleware_ShouldReturn401_WhenTokenIssuedBeforeInvalidation()
    {
        var userId = Guid.NewGuid();
        var user = new User("testuser", "test@example.com", BCrypt.Net.BCrypt.HashPassword("password"));
        SetUserId(user, userId);

        var tokenIssuedAt = DateTimeOffset.UtcNow.AddHours(-2);
        user.InvalidateTokens();

        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var userRepoMock = new Mock<IUserRepository>();
        unitOfWorkMock.Setup(u => u.Users).Returns(userRepoMock.Object);
        userRepoMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var middleware = new TokenValidationMiddleware(next, NullLogger<TokenValidationMiddleware>.Instance);

        var context = CreateAuthenticatedContext(userId, tokenIssuedAt);
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context, unitOfWorkMock.Object);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);

        var body = await ReadResponseBody(context);
        body.Message.Should().Contain("invalidated");
    }

    [Fact]
    public async Task TokenValidationMiddleware_ShouldPassThrough_WhenTokenIssuedAfterReauthentication()
    {
        var userId = Guid.NewGuid();
        var user = new User("testuser", "test@example.com", BCrypt.Net.BCrypt.HashPassword("password"));
        SetUserId(user, userId);

        user.InvalidateTokens();

        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var userRepoMock = new Mock<IUserRepository>();
        unitOfWorkMock.Setup(u => u.Users).Returns(userRepoMock.Object);
        userRepoMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var middleware = new TokenValidationMiddleware(next, NullLogger<TokenValidationMiddleware>.Instance);

        var tokenIssuedAt = DateTimeOffset.UtcNow.AddSeconds(2);
        var context = CreateAuthenticatedContext(userId, tokenIssuedAt);

        await middleware.InvokeAsync(context, unitOfWorkMock.Object);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task TokenValidationMiddleware_ShouldPassThrough_WhenClaimsHaveNoUserId()
    {
        var claims = new List<Claim> { new("username", "testuser") };
        var identity = new ClaimsIdentity(claims, "Bearer");
        var principal = new ClaimsPrincipal(identity);

        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var middleware = new TokenValidationMiddleware(next, NullLogger<TokenValidationMiddleware>.Instance);

        var context = new DefaultHttpContext { User = principal };

        await middleware.InvokeAsync(context, unitOfWorkMock.Object);

        nextCalled.Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────
    // Login controller-level edge cases
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_ShouldReturn401_WhenBodyIsNull()
    {
        var (uow, authService) = CreateMockAuthServiceWithUow();
        var controller = new AuthController(authService.Object, CreateGitHubSettings(false), CreateMockUserContext().Object, uow.Object);

        var result = await controller.Login(null);

        var unauthorized = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
        var error = unauthorized.Value.Should().BeOfType<ApiErrorResponse>().Subject;
        error.ErrorCode.Should().Be(ErrorCodes.AuthenticationFailed);
    }

    [Fact]
    public async Task Login_ShouldReturn401_WhenFieldsEmpty()
    {
        var (uow, authService) = CreateMockAuthServiceWithUow();
        var controller = new AuthController(authService.Object, CreateGitHubSettings(false), CreateMockUserContext().Object, uow.Object);

        var result = await controller.Login(new LoginDto("", ""));

        var unauthorized = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
        var error = unauthorized.Value.Should().BeOfType<ApiErrorResponse>().Subject;
        error.ErrorCode.Should().Be(ErrorCodes.AuthenticationFailed);
    }

    // ─────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────

    private static (AuthController Controller, Mock<IUnitOfWork> UnitOfWork) CreateAuthControllerWithUnitOfWork(bool gitHubConfigured = false)
    {
        var (unitOfWorkMock, authServiceMock) = CreateMockAuthServiceWithUow();
        var gitHubSettings = CreateGitHubSettings(gitHubConfigured);

        // Set up default OAuthAuthCodes repo that returns null (code not found)
        var authCodeRepoMock = new Mock<IOAuthAuthCodeRepository>();
        authCodeRepoMock.Setup(r => r.GetByCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OAuthAuthCode?)null);
        unitOfWorkMock.Setup(u => u.OAuthAuthCodes).Returns(authCodeRepoMock.Object);

        var controller = new AuthController(authServiceMock.Object, gitHubSettings, CreateMockUserContext().Object, unitOfWorkMock.Object);
        return (controller, unitOfWorkMock);
    }

    private static (Mock<IUnitOfWork> UnitOfWork, Mock<AuthenticationService> AuthService) CreateMockAuthServiceWithUow()
    {
        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var userRepoMock = new Mock<IUserRepository>();
        unitOfWorkMock.Setup(u => u.Users).Returns(userRepoMock.Object);
        unitOfWorkMock.Setup(u => u.ExternalLogins).Returns(new Mock<IExternalLoginRepository>().Object);

        var authServiceMock = new Mock<AuthenticationService>(unitOfWorkMock.Object, DefaultJwtSettings) { CallBase = true };
        return (unitOfWorkMock, authServiceMock);
    }

    private static Mock<IUserContext> CreateMockUserContext(bool authenticated = true, Guid? userId = null)
    {
        var mock = new Mock<IUserContext>();
        mock.Setup(u => u.IsAuthenticated).Returns(authenticated);
        mock.Setup(u => u.UserId).Returns(authenticated ? (userId ?? Guid.NewGuid()).ToString() : null!);
        return mock;
    }

    private static GitHubOAuthSettings CreateGitHubSettings(bool configured)
    {
        return configured
            ? new GitHubOAuthSettings { ClientId = "test-client", ClientSecret = "test-secret" }
            : new GitHubOAuthSettings();
    }

    private static OAuthAuthCode CreateExpiredAuthCode(string code)
    {
        // Create a valid code first, then set ExpiresAt to the past via reflection
        var authCode = new OAuthAuthCode(code, Guid.NewGuid(), "fake-token", DateTimeOffset.UtcNow.AddSeconds(60));
        var expiresAtProp = typeof(OAuthAuthCode).GetProperty("ExpiresAt");
        expiresAtProp!.SetValue(authCode, DateTimeOffset.UtcNow.AddSeconds(-10));
        return authCode;
    }

    private static DefaultHttpContext CreateAuthenticatedContext(Guid userId, DateTimeOffset? iat)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString())
        };

        if (iat.HasValue)
        {
            claims.Add(new Claim(
                JwtRegisteredClaimNames.Iat,
                iat.Value.ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64));
        }

        var identity = new ClaimsIdentity(claims, "Bearer");
        var principal = new ClaimsPrincipal(identity);

        return new DefaultHttpContext { User = principal };
    }

    private static void SetUserId(User user, Guid userId)
    {
        var idProperty = typeof(Domain.Common.Entity).GetProperty("Id");
        idProperty!.SetValue(user, userId);
    }

    private static async Task<ApiErrorResponse> ReadResponseBody(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var json = await reader.ReadToEndAsync();
        return JsonSerializer.Deserialize<ApiErrorResponse>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;
    }
}
