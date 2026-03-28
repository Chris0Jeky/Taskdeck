using FluentAssertions;
using Taskdeck.Api.Workers;
using Xunit;

namespace Taskdeck.Api.Tests;

public class WorkerHeartbeatRegistryTests
{
    [Fact]
    public void Constructor_ShouldInitializeStartupTime()
    {
        var before = DateTimeOffset.UtcNow;

        var registry = new WorkerHeartbeatRegistry();

        registry.StartupTime.Should().BeOnOrAfter(before);
        registry.StartupTime.Should().BeOnOrBefore(DateTimeOffset.UtcNow);
    }

    [Fact]
    public void ReportHeartbeat_ShouldRegisterWorkerHeartbeat()
    {
        var registry = new WorkerHeartbeatRegistry();
        var before = DateTimeOffset.UtcNow;

        registry.ReportHeartbeat("proposal-worker");

        var heartbeat = registry.GetLastHeartbeat("proposal-worker");
        heartbeat.Should().NotBeNull();
        heartbeat.Should().BeOnOrAfter(before);
    }

    [Fact]
    public async Task ReportHeartbeat_ShouldUpdateExistingWorkerHeartbeat()
    {
        var registry = new WorkerHeartbeatRegistry();
        registry.ReportHeartbeat("proposal-worker");
        var firstHeartbeat = registry.GetLastHeartbeat("proposal-worker");

        await Task.Delay(50);

        registry.ReportHeartbeat("proposal-worker");

        var secondHeartbeat = registry.GetLastHeartbeat("proposal-worker");
        secondHeartbeat.Should().NotBeNull();
        secondHeartbeat.Should().BeAfter(firstHeartbeat!.Value);
    }

    [Fact]
    public void GetLastHeartbeat_ShouldReturnNull_WhenWorkerHasNotReported()
    {
        var registry = new WorkerHeartbeatRegistry();

        var heartbeat = registry.GetLastHeartbeat("missing-worker");

        heartbeat.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void ReportHeartbeat_ShouldIgnoreBlankWorkerNames(string workerName)
    {
        var registry = new WorkerHeartbeatRegistry();

        registry.ReportHeartbeat(workerName);

        registry.GetLastHeartbeat(workerName).Should().BeNull();
    }

    [Fact]
    public void ReportHeartbeat_ShouldIgnoreNullWorkerName()
    {
        var registry = new WorkerHeartbeatRegistry();

        registry.ReportHeartbeat(null!);

        // Verify nothing was registered by checking a known-good name
        registry.GetLastHeartbeat("any-worker").Should().BeNull();
    }

    [Fact]
    public void ReportHeartbeat_ShouldTrackMultipleWorkersIndependently()
    {
        var registry = new WorkerHeartbeatRegistry();

        registry.ReportHeartbeat("proposal-worker");
        registry.ReportHeartbeat("delivery-worker");

        registry.GetLastHeartbeat("proposal-worker").Should().NotBeNull();
        registry.GetLastHeartbeat("delivery-worker").Should().NotBeNull();
    }

}
