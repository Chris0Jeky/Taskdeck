using System.Collections.Concurrent;
using System.Data.Common;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Middleware;
using Taskdeck.Api.RateLimiting;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Unit tests for the #1384 fix: <see cref="ApiKeyMiddleware"/> enforces the per-key request budget
/// (<see cref="McpPerApiKeyRateLimiter"/>) immediately after the key row is resolved and confirmed
/// active, BEFORE the user-account lookup and the <c>UpdateLastUsedAsync</c> write. A valid but
/// over-quota key must be rejected with the standard 429 contract without performing that
/// authentication-stage database work, and the budget must be charged exactly once per admitted
/// request (no double-charge, since the endpoint-stage policy is gone). Drives the real middleware
/// against a real SQLite <see cref="TaskdeckDbContext"/> with a command interceptor so the absence of
/// the Users query and the last-used UPDATE is observed at the SQL boundary, not merely asserted.
/// </summary>
public sealed class ApiKeyMiddlewarePerKeyRateLimitTests : IDisposable
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"taskdeck-perkey-{Guid.NewGuid():N}.db");
    private readonly CapturingCommandInterceptor _interceptor = new();

    [Fact]
    public async Task OverQuotaValidKey_Returns429_BeforeUserLookupAndUsageWrite()
    {
        await using var db = await CreateSeededContextAsync();
        const string plaintext = "tdsk_perkey_overquota_000000000000000000";
        var (_, apiKeyId) = await SeedUserAndKeyAsync(db, plaintext);

        // Budget of one, already spent for this key's partition, so the next admitted request is over
        // quota. Pre-spend via a context carrying the same key-id partition the middleware will use.
        using var limiter = new McpPerApiKeyRateLimiter(new RateLimitPolicySettings(1, 60));
        using (var preSpend = limiter.AttemptAcquire(ContextForPartition(apiKeyId)))
        {
            preSpend.IsAcquired.Should().BeTrue("the single permit is consumed to exhaust the budget");
        }

        var nextCalled = false;
        var middleware = new ApiKeyMiddleware(_ => { nextCalled = true; return Task.CompletedTask; },
            NullLogger<ApiKeyMiddleware>.Instance);

        var context = CreateMcpContext(plaintext, limiter);
        _interceptor.Clear(); // discard the seed SQL; capture only the request's queries.

        await middleware.InvokeAsync(context, db);

        // Rejected with the per-key 429 contract, before the pipeline continued.
        context.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        nextCalled.Should().BeFalse("an over-quota request must not reach the MCP endpoint");

        // The key lookup (ApiKeys SELECT) is unavoidable — it is how the key ID is known — but the
        // Users lookup and the last-used UPDATE must NOT have run: that is the auth-stage database work
        // #1384 shields from a valid-but-over-quota key.
        _interceptor.Captured.Should().Contain(
            sql => IsSelect(sql) && sql.Contains("ApiKeys", StringComparison.OrdinalIgnoreCase),
            "the key row is resolved before the budget check");
        _interceptor.Captured.Should().NotContain(
            sql => IsSelect(sql) && sql.Contains("Users", StringComparison.OrdinalIgnoreCase),
            "the over-quota request must be rejected before the user-account lookup");
        _interceptor.Captured.Should().NotContain(
            sql => IsApiKeysUpdate(sql),
            "the over-quota request must be rejected before the UpdateLastUsedAsync write");

        // A per-key 429 is NOT an authentication failure: the pre-auth IP failure budget marker must
        // stay unset so valid keys never spend that budget (#1368/#1381).
        context.Items.ContainsKey(ApiKeyMiddleware.AuthenticationFailedItemKey).Should().BeFalse();

        // 429 contract mirrors the former endpoint policy exactly.
        context.Response.ContentType.Should().StartWith("application/json");
        context.Response.Headers["Retry-After"].ToString().Should().NotBeNullOrWhiteSpace();
        context.Response.Headers["X-RateLimit-Policy"].ToString()
            .Should().Be(RateLimitingPolicyNames.McpPerApiKey);
        var error = await ReadErrorAsync(context);
        error.Should().NotBeNull();
        error!.ErrorCode.Should().Be(ErrorCodes.TooManyRequests);
        error.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task UnderQuotaValidKey_PassesThrough_ReachingUserLookup()
    {
        await using var db = await CreateSeededContextAsync();
        const string plaintext = "tdsk_perkey_underquota_00000000000000000";
        await SeedUserAndKeyAsync(db, plaintext);

        using var limiter = new McpPerApiKeyRateLimiter(new RateLimitPolicySettings(5, 60));
        var nextCalled = false;
        var middleware = new ApiKeyMiddleware(_ => { nextCalled = true; return Task.CompletedTask; },
            NullLogger<ApiKeyMiddleware>.Instance);

        var context = CreateMcpContext(plaintext, limiter);
        _interceptor.Clear();

        await middleware.InvokeAsync(context, db);

        nextCalled.Should().BeTrue("an under-quota valid key must pass through to the MCP endpoint");
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK, "the terminal delegate left the default status");
        // The mirror of the over-quota assertion: an admitted request DOES reach the user-account
        // lookup that the over-quota path skips, confirming the per-key gate is what shields it.
        _interceptor.Captured.Should().Contain(
            sql => IsSelect(sql) && sql.Contains("Users", StringComparison.OrdinalIgnoreCase),
            "an admitted request performs the user-account lookup the over-quota path skips");
    }

    [Fact]
    public async Task PerKeyBudget_AdmitsExactlyPermitLimitRequests_ThenRejects_NoDoubleCharge()
    {
        await using var db = await CreateSeededContextAsync();
        const string plaintext = "tdsk_perkey_countbudget_0000000000000000";
        await SeedUserAndKeyAsync(db, plaintext);

        const int permitLimit = 3;
        using var limiter = new McpPerApiKeyRateLimiter(new RateLimitPolicySettings(permitLimit, 60));
        var middleware = new ApiKeyMiddleware(_ => Task.CompletedTask, NullLogger<ApiKeyMiddleware>.Instance);

        // Exactly permitLimit requests are admitted (each charges one permit, once); the next is 429.
        // If any admitted request were double-charged, fewer than permitLimit would succeed.
        for (var i = 0; i < permitLimit; i++)
        {
            var admitted = CreateMcpContext(plaintext, limiter);
            await middleware.InvokeAsync(admitted, db);
            admitted.Response.StatusCode.Should().NotBe(StatusCodes.Status429TooManyRequests,
                $"request {i + 1} of {permitLimit} is within the budget");
        }

        var rejected = CreateMcpContext(plaintext, limiter);
        await middleware.InvokeAsync(rejected, db);
        rejected.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests,
            "the request past the per-key budget is rejected");
        rejected.Response.Headers["X-RateLimit-Policy"].ToString()
            .Should().Be(RateLimitingPolicyNames.McpPerApiKey);
    }

    // ── Helpers ──

    private static bool IsSelect(string sql) => sql.Contains("SELECT", StringComparison.OrdinalIgnoreCase);

    // Match a genuine UPDATE statement against ApiKeys (the UpdateLastUsedAsync write), not a SELECT
    // that merely lists the "UpdatedAt" column — hence StartsWith on the trimmed command text.
    private static bool IsApiKeysUpdate(string sql) =>
        sql.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
        && sql.Contains("ApiKeys", StringComparison.OrdinalIgnoreCase);

    private async Task<TaskdeckDbContext> CreateSeededContextAsync()
    {
        var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .AddInterceptors(_interceptor)
            .Options;
        var db = new TaskdeckDbContext(options);
        await db.Database.MigrateAsync();
        return db;
    }

    private static async Task<(Guid userId, Guid apiKeyId)> SeedUserAndKeyAsync(
        TaskdeckDbContext db,
        string plaintextKey)
    {
        var user = new User("perkey-user-" + Guid.NewGuid().ToString("N")[..8], $"{Guid.NewGuid():N}@example.com", "hash");
        db.Users.Add(user);
        var apiKey = new ApiKey(user.Id, ApiKeyService.HashKey(plaintextKey), plaintextKey[..8], "Per-key test");
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

    private static DefaultHttpContext ContextForPartition(Guid apiKeyId)
    {
        var context = new DefaultHttpContext();
        context.Items[ApiKeyMiddleware.ApiKeyIdItemKey] = apiKeyId;
        return context;
    }

    private static async Task<ApiErrorResponse?> ReadErrorAsync(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        return await JsonSerializer.DeserializeAsync<ApiErrorResponse>(context.Response.Body, WebJson);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
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

    private sealed class CapturingCommandInterceptor : DbCommandInterceptor
    {
        private readonly ConcurrentQueue<string> _commands = new();

        public IReadOnlyCollection<string> Captured => _commands;

        public void Clear() => _commands.Clear();

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            _commands.Enqueue(command.CommandText);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            _commands.Enqueue(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            _commands.Enqueue(command.CommandText);
            return base.NonQueryExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            _commands.Enqueue(command.CommandText);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
