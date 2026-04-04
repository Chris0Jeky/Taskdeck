using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
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
    public void ExchangeCode_ShouldReturn400_WhenCodeIsEmpty()
    {
        var controller = CreateAuthController();
        var result = controller.ExchangeCode(new ExchangeCodeRequest(string.Empty));

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<ApiErrorResponse>().Subject;
        error.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public void ExchangeCode_ShouldReturn401_WhenCodeIsInvalid()
    {
        var controller = CreateAuthController();
        var result = controller.ExchangeCode(new ExchangeCodeRequest("nonexistent-code"));

        var unauthorized = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
        var error = unauthorized.Value.Should().BeOfType<ApiErrorResponse>().Subject;
        error.ErrorCode.Should().Be(ErrorCodes.AuthenticationFailed);
    }

    [Fact]
    public void ExchangeCode_ShouldPreventReplay_SecondUseOfSameCode()
    {
        // Insert a code into the static dictionary via reflection
        var code = "test-replay-code";
        var authResult = new AuthResultDto("fake-token", new UserDto(
            Guid.NewGuid(), "user", "user@test.com",
            Domain.Enums.UserRole.Editor, true,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        InjectAuthCode(code, authResult, DateTimeOffset.UtcNow.AddSeconds(60));

        var controller = CreateAuthController();

        // First exchange — success
        var first = controller.ExchangeCode(new ExchangeCodeRequest(code));
        first.Should().BeOfType<OkObjectResult>();

        // Second exchange with same code — should fail (code was consumed)
        var second = controller.ExchangeCode(new ExchangeCodeRequest(code));
        second.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public void ExchangeCode_ShouldReturn401_WhenCodeHasExpired()
    {
        var code = "test-expired-code";
        var authResult = new AuthResultDto("fake-token", new UserDto(
            Guid.NewGuid(), "user", "user@test.com",
            Domain.Enums.UserRole.Editor, true,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        // Inject code that expired 10 seconds ago
        InjectAuthCode(code, authResult, DateTimeOffset.UtcNow.AddSeconds(-10));

        var controller = CreateAuthController();
        var result = controller.ExchangeCode(new ExchangeCodeRequest(code));

        // TryRemove succeeds (code existed), then the expiry check fires — returns "Code has expired"
        var unauthorized = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
        var error = unauthorized.Value.Should().BeOfType<ApiErrorResponse>().Subject;
        error.Message.Should().Contain("expired");
    }

    [Fact]
    public void GetProviders_ShouldReturnGitHubStatus()
    {
        var controller = CreateAuthController(gitHubConfigured: true);
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
        var controller = CreateAuthController(gitHubConfigured: false);
        var result = controller.GitHubLogin();

        var notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        var error = notFound.Value.Should().BeOfType<ApiErrorResponse>().Subject;
        error.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public void GitHubLogin_ShouldReturn400_WhenReturnUrlIsExternal()
    {
        var controller = CreateAuthController(gitHubConfigured: true);

        // The controller calls Url.IsLocalUrl which needs ActionContext setup.
        // We set up a mock URL helper that rejects external URLs.
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
    // TokenValidationMiddleware — account deletion during active session
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task TokenValidationMiddleware_ShouldReturn401_WhenUserDeletedDuringSession()
    {
        var userId = Guid.NewGuid();
        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var userRepoMock = new Mock<IUserRepository>();
        unitOfWorkMock.Setup(u => u.Users).Returns(userRepoMock.Object);

        // User is deleted — returns null
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

        // Token was issued 2 hours ago
        var tokenIssuedAt = DateTimeOffset.UtcNow.AddHours(-2);

        // Invalidation happened 1 hour ago
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
        // After invalidation, a freshly issued token should still work
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

        // Token issued 2 seconds after invalidation
        var tokenIssuedAt = DateTimeOffset.UtcNow.AddSeconds(2);
        var context = CreateAuthenticatedContext(userId, tokenIssuedAt);

        await middleware.InvokeAsync(context, unitOfWorkMock.Object);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task TokenValidationMiddleware_ShouldPassThrough_WhenClaimsHaveNoUserId()
    {
        // Token with authenticated identity but no parseable userId
        var claims = new List<Claim> { new("username", "testuser") };
        var identity = new ClaimsIdentity(claims, "Bearer");
        var principal = new ClaimsPrincipal(identity);

        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var middleware = new TokenValidationMiddleware(next, NullLogger<TokenValidationMiddleware>.Instance);

        var context = new DefaultHttpContext { User = principal };

        await middleware.InvokeAsync(context, unitOfWorkMock.Object);

        // Should pass through — let downstream handle it
        nextCalled.Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────
    // Login controller-level edge cases
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_ShouldReturn401_WhenBodyIsNull()
    {
        var authService = CreateMockAuthService();
        var controller = new AuthController(authService.Object, CreateGitHubSettings(false), new Mock<IUserContext>().Object);

        var result = await controller.Login(null);

        var unauthorized = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
        var error = unauthorized.Value.Should().BeOfType<ApiErrorResponse>().Subject;
        error.ErrorCode.Should().Be(ErrorCodes.AuthenticationFailed);
    }

    [Fact]
    public async Task Login_ShouldReturn401_WhenFieldsEmpty()
    {
        var authService = CreateMockAuthService();
        var controller = new AuthController(authService.Object, CreateGitHubSettings(false), new Mock<IUserContext>().Object);

        var result = await controller.Login(new LoginDto("", ""));

        var unauthorized = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
        var error = unauthorized.Value.Should().BeOfType<ApiErrorResponse>().Subject;
        error.ErrorCode.Should().Be(ErrorCodes.AuthenticationFailed);
    }

    // ─────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────

    private static AuthController CreateAuthController(bool gitHubConfigured = false)
    {
        var authServiceMock = CreateMockAuthService();
        var gitHubSettings = CreateGitHubSettings(gitHubConfigured);
        return new AuthController(authServiceMock.Object, gitHubSettings, new Mock<IUserContext>().Object);
    }

    private static Mock<AuthenticationService> CreateMockAuthService()
    {
        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var userRepoMock = new Mock<IUserRepository>();
        unitOfWorkMock.Setup(u => u.Users).Returns(userRepoMock.Object);
        unitOfWorkMock.Setup(u => u.ExternalLogins).Returns(new Mock<IExternalLoginRepository>().Object);

        // AuthenticationService is not sealed, but its constructor requires specific params
        return new Mock<AuthenticationService>(unitOfWorkMock.Object, DefaultJwtSettings) { CallBase = true };
    }

    private static GitHubOAuthSettings CreateGitHubSettings(bool configured)
    {
        return configured
            ? new GitHubOAuthSettings { ClientId = "test-client", ClientSecret = "test-secret" }
            : new GitHubOAuthSettings();
    }

    private static void InjectAuthCode(string code, AuthResultDto result, DateTimeOffset expiry)
    {
        // Access the static _authCodes field via reflection
        var field = typeof(AuthController).GetField("_authCodes",
            BindingFlags.NonPublic | BindingFlags.Static);
        var dict = (ConcurrentDictionary<string, (AuthResultDto Result, DateTimeOffset Expiry)>)field!.GetValue(null)!;
        dict[code] = (result, expiry);
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
