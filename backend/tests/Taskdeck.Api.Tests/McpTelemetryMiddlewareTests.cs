using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Taskdeck.Api.Mcp;
using Taskdeck.Api.Middleware;
using Taskdeck.Infrastructure.Mcp;
using Taskdeck.Tests.Support;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Tests for <see cref="McpTelemetryMiddleware"/>.
/// Verifies structured logging properties, correlation ID propagation,
/// and non-intrusive error handling.
/// </summary>
public class McpTelemetryMiddlewareTests
{
    private readonly InMemoryLogger<McpTelemetryMiddleware> _logger = new();

    [Fact]
    public async Task InvokeAsync_NonMcpPath_SkipsLogging()
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var context = CreateHttpContext("/api/boards");

        await middleware.InvokeAsync(context);

        _logger.Entries.Should().BeEmpty("non-MCP paths should not be logged by this middleware");
    }

    [Fact]
    public async Task InvokeAsync_McpPath_LogsStartAndCompletion()
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var context = CreateHttpContext("/mcp");

        await middleware.InvokeAsync(context);

        _logger.Entries.Should().HaveCount(2);
        _logger.Entries[0].Message.Should().Contain("MCP HTTP request started");
        _logger.Entries[1].Message.Should().Contain("MCP HTTP request completed");
    }

    [Fact]
    public async Task InvokeAsync_McpPath_IncludesCorrelationId()
    {
        var correlationId = "test-corr-123";
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var context = CreateHttpContext("/mcp");
        context.Items[CorrelationIdMiddleware.ItemKey] = correlationId;

        await middleware.InvokeAsync(context);

        _logger.Entries.Should().HaveCountGreaterThan(0);
        _logger.Entries[0].Message.Should().Contain(correlationId);
    }

    [Fact]
    public async Task InvokeAsync_McpPath_IncludesUserId()
    {
        var userId = Guid.NewGuid();
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var context = CreateHttpContext("/mcp");
        context.Items[HttpUserContextProvider.UserIdItemKey] = userId;

        await middleware.InvokeAsync(context);

        _logger.Entries[1].Message.Should().Contain(userId.ToString());
    }

    [Fact]
    public async Task InvokeAsync_McpPath_LogsStatusCode()
    {
        var middleware = CreateMiddleware(ctx =>
        {
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        });
        var context = CreateHttpContext("/mcp");

        await middleware.InvokeAsync(context);

        _logger.Entries[1].Message.Should().Contain("200");
    }

    [Fact]
    public async Task InvokeAsync_McpPath_LogsDuration()
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var context = CreateHttpContext("/mcp");

        await middleware.InvokeAsync(context);

        _logger.Entries[1].Message.Should().Contain("DurationMs");
    }

    [Fact]
    public async Task InvokeAsync_McpPath_WhenExceptionThrown_LogsErrorAndRethrows()
    {
        var expectedException = new InvalidOperationException("test failure");
        var middleware = CreateMiddleware(_ => throw expectedException);
        var context = CreateHttpContext("/mcp");

        var act = () => middleware.InvokeAsync(context);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _logger.Entries.Should().Contain(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Error);
        _logger.Entries.Should().Contain(e => e.Message.Contains("InvalidOperationException"));
    }

    [Fact]
    public async Task InvokeAsync_McpPath_WhenCancelled_LogsCancellationNotError()
    {
        var cts = new CancellationTokenSource();
        var middleware = CreateMiddleware(_ =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });
        var context = CreateHttpContext("/mcp");
        context.RequestAborted = cts.Token;

        var act = () => middleware.InvokeAsync(context);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _logger.Entries.Should().NotContain(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Error,
            "client disconnects should not be logged as errors");
        _logger.Entries.Should().Contain(e => e.Message.Contains("cancelled"),
            "client disconnects should be logged as cancellations");
    }

    [Fact]
    public async Task InvokeAsync_McpPath_NeverLogsApiKeyValues()
    {
        var apiKey = "tdsk_secretkey12345678";
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var context = CreateHttpContext("/mcp");
        context.Request.Headers.Authorization = $"Bearer {apiKey}";

        await middleware.InvokeAsync(context);

        foreach (var entry in _logger.Entries)
        {
            entry.Message.Should().NotContain(apiKey, "API key values must never appear in logs");
            entry.Message.Should().NotContain("tdsk_", "API key prefixes should not appear in MCP telemetry logs");
        }
    }

    [Fact]
    public async Task InvokeAsync_McpSubPath_StillLogs()
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var context = CreateHttpContext("/mcp/sse");

        await middleware.InvokeAsync(context);

        _logger.Entries.Should().HaveCountGreaterThan(0, "/mcp sub-paths should trigger logging");
    }

    private McpTelemetryMiddleware CreateMiddleware(RequestDelegate next)
    {
        return new McpTelemetryMiddleware(next, _logger);
    }

    private static DefaultHttpContext CreateHttpContext(string path, string method = "POST")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = method;
        return context;
    }
}
