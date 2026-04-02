using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Middleware;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Api.Tests;

public class TokenValidationMiddlewareTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IUserRepository> _userRepoMock;

    public TokenValidationMiddlewareTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _userRepoMock = new Mock<IUserRepository>();
        _unitOfWorkMock.Setup(u => u.Users).Returns(_userRepoMock.Object);
    }

    [Fact]
    public async Task InvokeAsync_ShouldPassThrough_WhenRequestIsUnauthenticated()
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var middleware = new TokenValidationMiddleware(next, NullLogger<TokenValidationMiddleware>.Instance);

        var context = new DefaultHttpContext();
        // No authenticated identity
        await middleware.InvokeAsync(context, _unitOfWorkMock.Object);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_ShouldPassThrough_WhenActiveUserHasValidToken()
    {
        var userId = Guid.NewGuid();
        var user = new User("testuser", "test@example.com", BCrypt.Net.BCrypt.HashPassword("password"));
        SetUserId(user, userId);

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var middleware = new TokenValidationMiddleware(next, NullLogger<TokenValidationMiddleware>.Instance);

        var issuedAt = DateTimeOffset.UtcNow;
        var context = CreateAuthenticatedContext(userId, issuedAt);

        await middleware.InvokeAsync(context, _unitOfWorkMock.Object);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().NotBe(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturn401_WhenUserIsInactive()
    {
        var userId = Guid.NewGuid();
        var user = new User("testuser", "test@example.com", BCrypt.Net.BCrypt.HashPassword("password"));
        SetUserId(user, userId);
        user.Deactivate();

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var middleware = new TokenValidationMiddleware(next, NullLogger<TokenValidationMiddleware>.Instance);

        var issuedAt = DateTimeOffset.UtcNow;
        var context = CreateAuthenticatedContext(userId, issuedAt);
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context, _unitOfWorkMock.Object);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);

        var body = await ReadResponseBody(context);
        body.ErrorCode.Should().Be(ErrorCodes.Unauthorized);
        body.Message.Should().Contain("inactive");
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturn401_WhenUserNotFound()
    {
        var userId = Guid.NewGuid();

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var middleware = new TokenValidationMiddleware(next, NullLogger<TokenValidationMiddleware>.Instance);

        var issuedAt = DateTimeOffset.UtcNow;
        var context = CreateAuthenticatedContext(userId, issuedAt);
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context, _unitOfWorkMock.Object);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturn401_WhenTokenIssuedBeforeInvalidation()
    {
        var userId = Guid.NewGuid();
        var user = new User("testuser", "test@example.com", BCrypt.Net.BCrypt.HashPassword("password"));
        SetUserId(user, userId);
        // Token was issued 1 hour ago
        var tokenIssuedAt = DateTimeOffset.UtcNow.AddHours(-1);
        // Invalidation happened 30 minutes ago (after token was issued)
        user.InvalidateTokens();

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var middleware = new TokenValidationMiddleware(next, NullLogger<TokenValidationMiddleware>.Instance);

        var context = CreateAuthenticatedContext(userId, tokenIssuedAt);
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context, _unitOfWorkMock.Object);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);

        var body = await ReadResponseBody(context);
        body.ErrorCode.Should().Be(ErrorCodes.Unauthorized);
        body.Message.Should().Contain("invalidated");
    }

    [Fact]
    public async Task InvokeAsync_ShouldPassThrough_WhenTokenIssuedAfterInvalidation()
    {
        var userId = Guid.NewGuid();
        var user = new User("testuser", "test@example.com", BCrypt.Net.BCrypt.HashPassword("password"));
        SetUserId(user, userId);
        // Invalidation happened first
        user.InvalidateTokens();

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var middleware = new TokenValidationMiddleware(next, NullLogger<TokenValidationMiddleware>.Instance);

        // Token issued 1 second after invalidation
        var tokenIssuedAt = DateTimeOffset.UtcNow.AddSeconds(1);
        var context = CreateAuthenticatedContext(userId, tokenIssuedAt);

        await middleware.InvokeAsync(context, _unitOfWorkMock.Object);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturn401WithApiErrorResponse_MatchingContract()
    {
        var userId = Guid.NewGuid();
        _userRepoMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var middleware = new TokenValidationMiddleware(next, NullLogger<TokenValidationMiddleware>.Instance);

        var context = CreateAuthenticatedContext(userId, DateTimeOffset.UtcNow);
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context, _unitOfWorkMock.Object);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        context.Response.ContentType.Should().StartWith("application/json");

        var body = await ReadResponseBody(context);
        body.ErrorCode.Should().Be(ErrorCodes.Unauthorized);
        body.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task InvokeAsync_ShouldPassThrough_WhenTokenIssuedInSameSecondAsInvalidation()
    {
        // Regression test for timestamp precision: InvalidateTokens() truncates to
        // whole seconds so a token issued in the same second is NOT rejected.
        var userId = Guid.NewGuid();
        var user = new User("testuser", "test@example.com", BCrypt.Net.BCrypt.HashPassword("password"));
        SetUserId(user, userId);
        user.InvalidateTokens();

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var middleware = new TokenValidationMiddleware(next, NullLogger<TokenValidationMiddleware>.Instance);

        // Token issued at the exact same second as invalidation
        var tokenIssuedAt = user.TokenInvalidatedAt!.Value;
        var context = CreateAuthenticatedContext(userId, tokenIssuedAt);

        await middleware.InvokeAsync(context, _unitOfWorkMock.Object);

        // Same-second token should pass through (not strictly "before" invalidation)
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_ShouldPassThrough_WhenNoIatClaimButUserIsActive()
    {
        var userId = Guid.NewGuid();
        var user = new User("testuser", "test@example.com", BCrypt.Net.BCrypt.HashPassword("password"));
        SetUserId(user, userId);
        user.InvalidateTokens();

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var middleware = new TokenValidationMiddleware(next, NullLogger<TokenValidationMiddleware>.Instance);

        // Create context with no iat claim
        var context = CreateAuthenticatedContext(userId, iat: null);

        await middleware.InvokeAsync(context, _unitOfWorkMock.Object);

        // Without iat claim, the comparison cannot be made — token passes through.
        // This is acceptable because all tokens generated by our AuthenticationService
        // include the iat claim. Legacy tokens without iat will expire naturally.
        nextCalled.Should().BeTrue();
    }

    private DefaultHttpContext CreateAuthenticatedContext(Guid userId, DateTimeOffset? iat)
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

        var context = new DefaultHttpContext();
        context.User = principal;

        return context;
    }

    private static void SetUserId(User user, Guid userId)
    {
        // Use reflection to set the Id since it's protected in Entity base class
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
