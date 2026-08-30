using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Taskdeck.Api.Middleware;
using Taskdeck.Tests.Support;
using Xunit;

namespace Taskdeck.Api.Tests;

public class UnhandledExceptionMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_ShouldNotEmit500_WhenRequestAbortedCancellationIsThrown()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var context = new DefaultHttpContext
        {
            RequestAborted = cts.Token
        };
        context.Response.Body = new MemoryStream();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/api/logs";

        RequestDelegate next = _ => throw new OperationCanceledException(cts.Token);
        var middleware = new UnhandledExceptionMiddleware(next, NullLogger<UnhandledExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().NotBe(StatusCodes.Status500InternalServerError);
        context.Response.Body.Length.Should().Be(0);
    }

    [Fact]
    public async Task InvokeAsync_ShouldLogOnlyMetadata_WhenCanceledRequestIsLogged()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var logger = new InMemoryLogger<UnhandledExceptionMiddleware>();
        var context = new DefaultHttpContext
        {
            RequestAborted = cts.Token
        };
        context.Response.Body = new MemoryStream();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/capture/items";
        context.TraceIdentifier = "req-cancel-redaction";

        RequestDelegate next = _ => throw new OperationCanceledException(
            "ordinary board title and arbitrary user content",
            cts.Token);
        var middleware = new UnhandledExceptionMiddleware(next, logger);

        await middleware.InvokeAsync(context);

        logger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Information);
        var entry = logger.Entries.Single(entry => entry.Level == LogLevel.Information);
        entry.Exception.Should().BeNull();
        entry.Message.Should().Contain("Request was canceled while processing POST /api/capture/items");
        entry.Message.Should().Contain("req-cancel-redaction");
        entry.Message.Should().Contain(nameof(OperationCanceledException));
        entry.Message.Should().NotContain("ordinary board title");
        entry.Message.Should().NotContain("arbitrary user content");
    }

    [Fact]
    public async Task InvokeAsync_ShouldLogOnlyMetadata_WhenLoggingUnhandledExceptions()
    {
        var logger = new InMemoryLogger<UnhandledExceptionMiddleware>();
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/capture/items";
        context.TraceIdentifier = "req-redaction";

        RequestDelegate next = _ => throw new InvalidOperationException(
            "ordinary board title and arbitrary user content");
        var middleware = new UnhandledExceptionMiddleware(next, logger);

        await middleware.InvokeAsync(context);

        logger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Error);
        var entry = logger.Entries.Single(entry => entry.Level == LogLevel.Error);
        entry.Exception.Should().BeNull();
        entry.Message.Should().Contain("Unhandled exception while processing POST /api/capture/items");
        entry.Message.Should().Contain("req-redaction");
        entry.Message.Should().Contain(nameof(InvalidOperationException));
        entry.Message.Should().Contain("ClassificationTruncated: False");
        entry.Message.Should().NotContain("ordinary board title");
        entry.Message.Should().NotContain("arbitrary user content");
    }

    [Fact]
    public async Task InvokeAsync_ShouldStripTerminalControlsFromRequestMetadata()
    {
        var logger = new InMemoryLogger<UnhandledExceptionMiddleware>();
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Method = "POST \u001Bescape\u000Bvertical\u009Bc1\r\nsafe café ✓";
        context.Request.Path = "/api/capture/items \u001Bescape\u000Bvertical\u009Bc1\r\nsafe café ✓";
        context.TraceIdentifier = "trace \u001Bescape\u000Bvertical\u009Bc1\r\nsafe café ✓";

        RequestDelegate next = _ => throw new InvalidOperationException("ignored content");
        var middleware = new UnhandledExceptionMiddleware(next, logger);

        await middleware.InvokeAsync(context);

        var message = logger.Entries.Single(entry => entry.Level == LogLevel.Error).Message;
        message.Should().NotContain("\u001B").And.NotContain("\u000B").And.NotContain("\u009B").And.NotContain("\r").And.NotContain("\n");
        message.Should().Contain("POST escapeverticalc1safe café ✓");
        message.Should().Contain("/api/capture/items escapeverticalc1safe café ✓");
        message.Should().Contain("trace escapeverticalc1safe café ✓");
    }
}
