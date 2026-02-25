using System.Diagnostics;
using FluentAssertions;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class LlmRequestAttributionMapperTests
{
    [Fact]
    public void ResolveCorrelationId_ShouldReturnGeneratedId_WhenValueIsMissing()
    {
        var resolved = LlmRequestAttributionMapper.ResolveCorrelationId(null);

        resolved.Should().HaveLength(32);
    }

    [Fact]
    public void ResolveCorrelationId_ShouldReturnGeneratedId_WhenValueContainsInvalidCharacters()
    {
        var resolved = LlmRequestAttributionMapper.ResolveCorrelationId("req-id\r\ninjected");

        resolved.Should().HaveLength(32);
        resolved.Should().NotContain("\r");
        resolved.Should().NotContain("\n");
    }

    [Fact]
    public void ResolveCorrelationId_ShouldClampValidValueToMaximumLength()
    {
        var correlationId = new string('a', 120);

        var resolved = LlmRequestAttributionMapper.ResolveCorrelationId(correlationId);

        resolved.Should().HaveLength(100);
        resolved.Should().Be(new string('a', 100));
    }

    [Fact]
    public void ResolveCorrelationIdFromActivity_ShouldUseTaggedCorrelationId()
    {
        using var activity = new Activity("attribution-test");
        activity.Start();
        activity.SetTag("taskdeck.correlation_id", "req/abc-123");

        var resolved = LlmRequestAttributionMapper.ResolveCorrelationIdFromActivity();

        resolved.Should().Be("req/abc-123");
    }

    [Fact]
    public void ResolveCorrelationIdFromActivity_ShouldFallbackToTraceId_WhenTagIsMissing()
    {
        using var activity = new Activity("attribution-test");
        activity.Start();

        var resolved = LlmRequestAttributionMapper.ResolveCorrelationIdFromActivity();

        resolved.Should().Be(activity.TraceId.ToString());
    }
}
