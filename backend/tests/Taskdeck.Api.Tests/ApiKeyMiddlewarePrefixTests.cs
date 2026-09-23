using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Taskdeck.Api.Middleware;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Pins the API-key format gate: the <c>tdsk_</c> prefix check is an exact
/// ordinal match, consistent with the middleware's other comparisons.
/// </summary>
public sealed class ApiKeyMiddlewarePrefixTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"taskdeck-prefix-{Guid.NewGuid():N}.db");

    [Theory]
    [InlineData("TDSK_000000000000000000000000000000000000")]
    [InlineData("Tdsk_000000000000000000000000000000000000")]
    [InlineData("tdsk")]
    [InlineData("bearer")]
    public async Task MalformedPrefix_Returns401WithFormatMessage(string token)
    {
        await using var db = await CreateMigratedContextAsync();
        var middleware = new ApiKeyMiddleware(
            _ => throw new InvalidOperationException("a malformed key must not reach the endpoint"),
            NullLogger<ApiKeyMiddleware>.Instance);
        var context = CreateMcpContext(token);

        await middleware.InvokeAsync(context, db);

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        (await ReadBodyAsync(context)).Should().Contain("Invalid API key format");
    }

    [Fact]
    public async Task WellFormedButUnknownKey_PassesFormatGateToLookup()
    {
        await using var db = await CreateMigratedContextAsync();
        var middleware = new ApiKeyMiddleware(
            _ => throw new InvalidOperationException("an unknown key must not reach the endpoint"),
            NullLogger<ApiKeyMiddleware>.Instance);
        var context = CreateMcpContext("tdsk_000000000000000000000000000000000000");

        await middleware.InvokeAsync(context, db);

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        (await ReadBodyAsync(context)).Should().NotContain("Invalid API key format");
    }

    private async Task<TaskdeckDbContext> CreateMigratedContextAsync()
    {
        var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        var db = new TaskdeckDbContext(options);
        await db.Database.MigrateAsync();
        return db;
    }

    private static DefaultHttpContext CreateMcpContext(string bearerKey)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().BuildServiceProvider()
        };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/mcp";
        context.Request.Headers.Authorization = $"Bearer {bearerKey}";
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<string> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        return await reader.ReadToEndAsync();
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
}
