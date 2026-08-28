using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Taskdeck.Api.Middleware;
using Taskdeck.Api.RateLimiting;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Regression tests for #1402: <see cref="ApiKeyMiddleware.UpdateLastUsedAsync"/> must actually
/// persist <see cref="ApiKey.LastUsedAt"/> (and <see cref="Taskdeck.Domain.Common.Entity.UpdatedAt"/>)
/// for an authenticated MCP request. Before the fix the <c>ExecuteUpdateAsync</c> set the nullable
/// <c>LastUsedAt</c> target to a non-nullable <c>DateTimeOffset.UtcNow</c>; EF Core's SQLite provider
/// refused to translate the resulting <c>(DateTimeOffset?)DateTimeOffset.UtcNow</c> value expression,
/// the whole UPDATE threw, and the failure was swallowed at Debug level — so LastUsedAt was silently
/// never written. These tests drive the real middleware against a real SQLite
/// <see cref="TaskdeckDbContext"/> and read the row back through a FRESH context (ExecuteUpdate bypasses
/// the change tracker), so a broken write cannot be masked by a tracked in-memory copy.
/// </summary>
public sealed class ApiKeyMiddlewareLastUsedPersistenceTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"taskdeck-lastused-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task AuthenticatedRequest_PersistsLastUsedAt_AsSaneUtcNow()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-5);
        // Backdate the seeded key's UpdatedAt to well outside the assertion window. The entity
        // constructor stamps UpdatedAt at seed time — moments before the middleware runs — so a
        // construction-time value would ALSO satisfy a "recent UTC-now" bound and the UpdatedAt
        // assertion could not fail even if the middleware's .SetProperty(k => k.UpdatedAt, ...)
        // line were deleted. Backdating forces the assertion to prove the value ADVANCED, i.e.
        // that the UPDATE statement itself wrote it.
        var backdatedUpdatedAt = DateTimeOffset.UtcNow.AddDays(-1);

        await using (var db = await CreateSeededContextAsync())
        {
            const string plaintext = "tdsk_lastused_persist_000000000000000000";
            var (_, apiKeyId) = await SeedUserAndKeyAsync(db, plaintext);
            // Raw SQL: UpdatedAt has a protected setter and Touch() always stamps "now", so the
            // backdate must go straight to the row.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE ApiKeys SET UpdatedAt = {backdatedUpdatedAt} WHERE Id = {apiKeyId}");

            var logger = new CapturingLogger();
            using var limiter = new McpPerApiKeyRateLimiter(new RateLimitPolicySettings(5, 60));
            var nextCalled = false;
            var middleware = new ApiKeyMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, logger);

            var context = CreateMcpContext(plaintext, limiter);
            await middleware.InvokeAsync(context, db);

            nextCalled.Should().BeTrue("a valid under-quota key must authenticate and pass through");
            context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);

            // The write must have succeeded silently: no swallowed failure was logged.
            logger.Entries.Should().NotContain(
                e => e.Contains("last-used", StringComparison.OrdinalIgnoreCase),
                "the last-used write must succeed, not be swallowed");
        }

        var after = DateTimeOffset.UtcNow.AddSeconds(5);

        // Read the row back through a fresh context: ExecuteUpdate writes straight to the database,
        // bypassing any change tracker, so this proves the value is actually persisted on disk.
        await using var verifyDb = await CreateContextAsync();
        var persisted = await verifyDb.ApiKeys.AsNoTracking().SingleAsync();

        persisted.LastUsedAt.Should().NotBeNull("an authenticated request must persist LastUsedAt (#1402)");
        persisted.LastUsedAt!.Value.Should().BeOnOrAfter(before).And.BeOnOrBefore(after,
            "the persisted timestamp must be a sane UTC-now instant");
        // Advancement past the backdated pre-call value (not merely "recent") is what proves the
        // UPDATE wrote UpdatedAt — see the backdating comment above.
        persisted.UpdatedAt.Should().BeAfter(backdatedUpdatedAt,
            "UpdatedAt must ADVANCE past the backdated pre-call value, proving the UPDATE wrote it");
        persisted.UpdatedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after,
            "UpdatedAt is written in the same statement and must not be discarded");
    }

    // ── Helpers ──

    private async Task<TaskdeckDbContext> CreateSeededContextAsync()
    {
        var db = await CreateContextAsync();
        await db.Database.MigrateAsync();
        return db;
    }

    private Task<TaskdeckDbContext> CreateContextAsync()
    {
        var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
            .UseSqlite(TestSqlite.ConnectionString(_dbPath))
            .Options;
        return Task.FromResult(new TaskdeckDbContext(options));
    }

    private static async Task<(Guid userId, Guid apiKeyId)> SeedUserAndKeyAsync(
        TaskdeckDbContext db,
        string plaintextKey)
    {
        var user = new User("lastused-user-" + Guid.NewGuid().ToString("N")[..8], $"{Guid.NewGuid():N}@example.com", "hash");
        db.Users.Add(user);
        var apiKey = new ApiKey(
            user.Id,
            ApiKeyService.HashKey(plaintextKey),
            plaintextKey[..8],
            "Last-used test",
            ApiKeyScope.Full);
        db.ApiKeys.Add(apiKey);
        await db.SaveChangesAsync();
        return (user.Id, apiKey.Id);
    }

    private static DefaultHttpContext CreateMcpContext(string bearerKey, McpPerApiKeyRateLimiter limiter)
    {
        var services = new ServiceCollection();
        services.AddSingleton(limiter);

        var context = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider()
        };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/mcp";
        context.Request.Headers.Authorization = $"Bearer {bearerKey}";
        context.Response.Body = new MemoryStream();
        return context;
    }

    public void Dispose()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
        {
            var path = _dbPath + suffix;
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // Best-effort temp cleanup; a leaked handle is not a test failure.
            }
        }
    }

    /// <summary>Records formatted log messages so the test can assert the write did not silently fail.</summary>
    private sealed class CapturingLogger : ILogger<ApiKeyMiddleware>
    {
        private readonly ConcurrentQueue<string> _entries = new();

        public IReadOnlyCollection<string> Entries => _entries;

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _entries.Enqueue(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
