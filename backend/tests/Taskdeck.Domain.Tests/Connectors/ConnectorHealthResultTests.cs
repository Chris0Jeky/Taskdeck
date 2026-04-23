using FluentAssertions;
using Taskdeck.Domain.Connectors;
using Xunit;

namespace Taskdeck.Domain.Tests.Connectors;

public class ConnectorHealthResultTests
{
    [Fact]
    public void Healthy_ShouldReturnHealthyStatus()
    {
        var result = ConnectorHealthResult.Healthy();

        result.Status.Should().Be(ConnectorHealthStatus.Healthy);
        result.Message.Should().Be("Provider is healthy.");
        result.CheckedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Healthy_ShouldAcceptCustomMessage()
    {
        var result = ConnectorHealthResult.Healthy("All systems go.");

        result.Status.Should().Be(ConnectorHealthStatus.Healthy);
        result.Message.Should().Be("All systems go.");
    }

    [Fact]
    public void Degraded_ShouldReturnDegradedStatus()
    {
        var result = ConnectorHealthResult.Degraded("Slow response.");

        result.Status.Should().Be(ConnectorHealthStatus.Degraded);
        result.Message.Should().Be("Slow response.");
    }

    [Fact]
    public void Unhealthy_ShouldReturnUnhealthyStatus()
    {
        var result = ConnectorHealthResult.Unhealthy("Service down.");

        result.Status.Should().Be(ConnectorHealthStatus.Unhealthy);
        result.Message.Should().Be("Service down.");
    }
}
