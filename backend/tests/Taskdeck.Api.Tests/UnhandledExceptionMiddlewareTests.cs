using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Taskdeck.Api.Middleware;
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
}
