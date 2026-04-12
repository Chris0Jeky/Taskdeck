using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Taskdeck.Api.Middleware;
using Taskdeck.Api.Services;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Api.Tests;

public class ActiveUserValidationMiddlewareTests
{
    private readonly InMemoryActiveUserCache _cache = new();
    private readonly NullLogger<ActiveUserValidationMiddleware> _logger = new();

    [Fact]
    public async Task InvokeAsync_PassesThrough_WhenNotAuthenticated()
    {
        var nextCalled = false;
        var middleware = new ActiveUserValidationMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            _logger);

        var unitOfWork = new StubUnitOfWork(null);
        var context = CreateHttpContext(authenticated: false, unitOfWork: unitOfWork);

        await middleware.InvokeAsync(context, _cache);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().NotBe(401);
    }

    [Fact]
    public async Task InvokeAsync_PassesThrough_WhenUserIdClaimMissing()
    {
        var nextCalled = false;
        var middleware = new ActiveUserValidationMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            _logger);

        var unitOfWork = new StubUnitOfWork(null);
        var context = CreateHttpContext(authenticated: true, userId: null, unitOfWork: unitOfWork);

        await middleware.InvokeAsync(context, _cache);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_PassesThrough_WhenUserIdClaimInvalid()
    {
        var nextCalled = false;
        var middleware = new ActiveUserValidationMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            _logger);

        var unitOfWork = new StubUnitOfWork(null);
        var context = CreateHttpContext(authenticated: true, userIdRaw: "not-a-guid", unitOfWork: unitOfWork);

        await middleware.InvokeAsync(context, _cache);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_PassesThrough_WhenActiveUserCached()
    {
        var userId = Guid.NewGuid();
        _cache.SetActiveStatus(userId, true);

        var nextCalled = false;
        var middleware = new ActiveUserValidationMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            _logger);

        var unitOfWork = new StubUnitOfWork(null); // should NOT be called
        var context = CreateHttpContext(authenticated: true, userId: userId, unitOfWork: unitOfWork);

        await middleware.InvokeAsync(context, _cache);

        nextCalled.Should().BeTrue();
        unitOfWork.GetByIdCallCount.Should().Be(0, "cache hit should prevent DB query");
    }

    [Fact]
    public async Task InvokeAsync_Returns401_WhenInactiveUserCached()
    {
        var userId = Guid.NewGuid();
        _cache.SetActiveStatus(userId, false);

        var nextCalled = false;
        var middleware = new ActiveUserValidationMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            _logger);

        var unitOfWork = new StubUnitOfWork(null);
        var context = CreateHttpContext(authenticated: true, userId: userId, unitOfWork: unitOfWork);

        await middleware.InvokeAsync(context, _cache);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task InvokeAsync_QueriesDb_OnCacheMiss_AndAllowsActiveUser()
    {
        var userId = Guid.NewGuid();
        var user = new User("testuser", "test@example.com", "hash123456");

        var cache = new InMemoryActiveUserCache(); // fresh cache, no entries
        var nextCalled = false;
        var middleware = new ActiveUserValidationMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            _logger);

        var unitOfWork = new StubUnitOfWork(user);
        var context = CreateHttpContext(authenticated: true, userId: userId, unitOfWork: unitOfWork);

        await middleware.InvokeAsync(context, cache);

        nextCalled.Should().BeTrue();
        unitOfWork.GetByIdCallCount.Should().Be(1);
        cache.GetCachedActiveStatus(userId).Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_Returns401_OnCacheMiss_WhenUserNotFound()
    {
        var userId = Guid.NewGuid();
        var cache = new InMemoryActiveUserCache();

        var nextCalled = false;
        var middleware = new ActiveUserValidationMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            _logger);

        var unitOfWork = new StubUnitOfWork(null);
        var context = CreateHttpContext(authenticated: true, userId: userId, unitOfWork: unitOfWork);

        await middleware.InvokeAsync(context, cache);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(401);
        cache.GetCachedActiveStatus(userId).Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_Returns401_OnCacheMiss_WhenUserInactive()
    {
        var userId = Guid.NewGuid();
        var user = new User("deactivated", "deactivated@example.com", "hash123456");
        user.Deactivate();

        var cache = new InMemoryActiveUserCache();
        var nextCalled = false;
        var middleware = new ActiveUserValidationMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            _logger);

        var unitOfWork = new StubUnitOfWork(user);
        var context = CreateHttpContext(authenticated: true, userId: userId, unitOfWork: unitOfWork);

        await middleware.InvokeAsync(context, cache);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(401);
        cache.GetCachedActiveStatus(userId).Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_Returns401_WithApiErrorResponseContract()
    {
        var userId = Guid.NewGuid();
        _cache.SetActiveStatus(userId, false);

        var middleware = new ActiveUserValidationMiddleware(
            _ => Task.CompletedTask,
            _logger);

        var unitOfWork = new StubUnitOfWork(null);
        var context = CreateHttpContext(authenticated: true, userId: userId, unitOfWork: unitOfWork);
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context, _cache);

        context.Response.StatusCode.Should().Be(401);
        context.Response.ContentType.Should().Contain("application/json");

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var doc = await JsonDocument.ParseAsync(context.Response.Body);
        var root = doc.RootElement;
        root.TryGetProperty("errorCode", out var errorCode).Should().BeTrue();
        errorCode.GetString().Should().Be("Unauthorized");
        root.TryGetProperty("message", out var message).Should().BeTrue();
        message.GetString().Should().Contain("deactivated");
    }

    [Fact]
    public async Task InvokeAsync_DoesNotResolveUnitOfWork_OnCacheHit()
    {
        // Verify that IUnitOfWork is NOT resolved from the service provider when cache hits.
        // This confirms the lazy-resolution optimization.
        var userId = Guid.NewGuid();
        _cache.SetActiveStatus(userId, true);

        var nextCalled = false;
        var middleware = new ActiveUserValidationMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            _logger);

        // Register no IUnitOfWork — resolving it would throw
        var context = CreateHttpContext(authenticated: true, userId: userId, unitOfWork: null);

        await middleware.InvokeAsync(context, _cache);

        nextCalled.Should().BeTrue("active cached user should pass through without DB lookup");
    }

    private static HttpContext CreateHttpContext(
        bool authenticated,
        Guid? userId = null,
        string? userIdRaw = null,
        StubUnitOfWork? unitOfWork = null)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        // Set up DI service provider with IUnitOfWork for lazy resolution
        var services = new ServiceCollection();
        if (unitOfWork is not null)
        {
            services.AddSingleton<IUnitOfWork>(unitOfWork);
        }
        context.RequestServices = services.BuildServiceProvider();

        if (!authenticated)
            return context;

        var claims = new List<Claim>();

        if (userId.HasValue)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()));
        }
        else if (userIdRaw is not null)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userIdRaw));
        }

        var identity = new ClaimsIdentity(claims, "TestScheme");
        context.User = new ClaimsPrincipal(identity);

        return context;
    }

    /// <summary>
    /// Minimal stub for IUnitOfWork that only supports Users.GetByIdAsync.
    /// Avoids a Moq dependency in the API test project.
    /// </summary>
    private sealed class StubUnitOfWork : IUnitOfWork
    {
        private readonly StubUserRepository _users;

        public StubUnitOfWork(User? userToReturn)
        {
            _users = new StubUserRepository(userToReturn);
        }

        public int GetByIdCallCount => _users.GetByIdCallCount;

        public IUserRepository Users => _users;

        // All other properties throw — the middleware should never access them.
        public IBoardRepository Boards => throw new NotImplementedException();
        public IColumnRepository Columns => throw new NotImplementedException();
        public ICardRepository Cards => throw new NotImplementedException();
        public ICardCommentRepository CardComments => throw new NotImplementedException();
        public ILabelRepository Labels => throw new NotImplementedException();
        public IBoardAccessRepository BoardAccesses => throw new NotImplementedException();
        public IAuditLogRepository AuditLogs => throw new NotImplementedException();
        public ILlmQueueRepository LlmQueue => throw new NotImplementedException();
        public IAutomationProposalRepository AutomationProposals => throw new NotImplementedException();
        public IArchiveItemRepository ArchiveItems => throw new NotImplementedException();
        public IChatSessionRepository ChatSessions => throw new NotImplementedException();
        public IChatMessageRepository ChatMessages => throw new NotImplementedException();
        public ICommandRunRepository CommandRuns => throw new NotImplementedException();
        public INotificationRepository Notifications => throw new NotImplementedException();
        public INotificationPreferenceRepository NotificationPreferences => throw new NotImplementedException();
        public IUserPreferenceRepository UserPreferences => throw new NotImplementedException();
        public IOutboundWebhookSubscriptionRepository OutboundWebhookSubscriptions => throw new NotImplementedException();
        public IOutboundWebhookDeliveryRepository OutboundWebhookDeliveries => throw new NotImplementedException();
        public ILlmUsageRecordRepository LlmUsageRecords => throw new NotImplementedException();
        public IAgentProfileRepository AgentProfiles => throw new NotImplementedException();
        public IAgentRunRepository AgentRuns => throw new NotImplementedException();
        public IKnowledgeDocumentRepository KnowledgeDocuments => throw new NotImplementedException();
        public IKnowledgeChunkRepository KnowledgeChunks => throw new NotImplementedException();
        public IExternalLoginRepository ExternalLogins => throw new NotImplementedException();
        public IOAuthAuthCodeRepository OAuthAuthCodes => throw new NotImplementedException();
        public IApiKeyRepository ApiKeys => throw new NotImplementedException();
        public IMfaCredentialRepository MfaCredentials => throw new NotImplementedException();

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubUserRepository : IUserRepository
    {
        private readonly User? _userToReturn;

        public StubUserRepository(User? userToReturn)
        {
            _userToReturn = userToReturn;
        }

        public int GetByIdCallCount { get; private set; }

        public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            GetByIdCallCount++;
            return Task.FromResult(_userToReturn);
        }

        public Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<bool> ExistsAsync(string username, string email, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<IEnumerable<User>> GetAllAsync(CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<User> AddAsync(User entity, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task UpdateAsync(User entity, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task DeleteAsync(User entity, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }
}
