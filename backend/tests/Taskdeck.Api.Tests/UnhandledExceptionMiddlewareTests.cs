using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Taskdeck.Api.Middleware;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.Services;
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
    public async Task InvokeAsync_ShouldRedactSensitiveDetails_WhenCanceledRequestIsLogged()
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
            "Authorization: Bearer cancel-secret {\"text\":\"capture secret\"} token=cancel-token",
            cts.Token);
        var middleware = new UnhandledExceptionMiddleware(next, logger);

        await middleware.InvokeAsync(context);

        logger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Information);
        var entry = logger.Entries.Single(entry => entry.Level == LogLevel.Information);
        entry.Exception.Should().BeNull();
        entry.Message.Should().Contain("Request was canceled while processing POST /api/capture/items");
        entry.Message.Should().Contain("req-cancel-redaction");
        entry.Message.Should().Contain($"Authorization: Bearer {SensitiveDataRedactor.RedactedValue}");
        entry.Message.Should().NotContain("cancel-secret");
        entry.Message.Should().NotContain("capture secret");
        entry.Message.Should().NotContain("cancel-token");
    }

    [Fact]
    public async Task InvokeAsync_ShouldRedactSensitiveDetails_WhenLoggingUnhandledExceptions()
    {
        var logger = new InMemoryLogger<UnhandledExceptionMiddleware>();
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/capture/items";
        context.TraceIdentifier = "req-redaction";

        RequestDelegate next = _ => throw new InvalidOperationException(
            "Authorization: Bearer super-secret {\"text\":\"capture secret\"} token=queue-secret");
        var middleware = new UnhandledExceptionMiddleware(next, logger);

        await middleware.InvokeAsync(context);

        logger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Error);
        var entry = logger.Entries.Single(entry => entry.Level == LogLevel.Error);
        entry.Exception.Should().BeNull();
        entry.Message.Should().Contain("Unhandled exception while processing POST /api/capture/items");
        entry.Message.Should().Contain("req-redaction");
        entry.Message.Should().Contain($"Authorization: Bearer {SensitiveDataRedactor.RedactedValue}");
        entry.Message.Should().NotContain("super-secret");
        entry.Message.Should().NotContain("capture secret");
        entry.Message.Should().NotContain("queue-secret");
    }
}
