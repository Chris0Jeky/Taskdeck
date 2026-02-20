using System.Diagnostics;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Taskdeck.Api.Middleware;
using Taskdeck.Api.Telemetry;
using Xunit;

namespace Taskdeck.Api.Tests;

public class CorrelationIdMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_ShouldAttachCorrelationTagsToCurrentActivity()
    {
        const string requestId = "req-test-correlation-id";
        var middleware = new CorrelationIdMiddleware(
            _ => Task.CompletedTask,
            NullLogger<CorrelationIdMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = requestId;

        using var activity = new Activity("test.request");
        activity.Start();
        await middleware.InvokeAsync(context);

        activity.GetTagItem(TaskdeckTelemetryTags.CorrelationId).Should().Be(requestId);
        activity.GetTagItem(TaskdeckTelemetryTags.RequestId).Should().Be(requestId);
        context.TraceIdentifier.Should().Be(requestId);
        context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString().Should().Be(requestId);
    }
}
