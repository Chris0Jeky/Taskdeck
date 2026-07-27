using FluentAssertions;
using Xunit;

namespace Taskdeck.Integration.Tests.Fixtures;

public sealed class DockerAvailableCheckTests
{
    [Fact]
    public void Timed_out_probe_kills_the_process_tree_and_reaps_it()
    {
        var operations = new List<string>();
        var waitTimeouts = new List<TimeSpan>();

        var isAvailable = DockerAvailableCheck.CheckDocker(
            startProcess: () => operations.Add("start"),
            waitForExit: timeout =>
            {
                operations.Add(waitTimeouts.Count == 0 ? "wait:probe" : "wait:reap");
                waitTimeouts.Add(timeout);
                return waitTimeouts.Count > 1;
            },
            getExitCode: () => throw new InvalidOperationException(
                "ExitCode must not be read after a timeout."),
            killProcessTree: () => operations.Add("kill"),
            probeTimeout: TimeSpan.FromSeconds(10),
            reapTimeout: TimeSpan.FromSeconds(2));

        isAvailable.Should().BeFalse();
        operations.Should().Equal("start", "wait:probe", "kill", "wait:reap");
        waitTimeouts.Should().Equal(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Timed_out_probe_handles_an_already_exited_process_and_still_reaps()
    {
        var waitInvocationCount = 0;

        var check = () => DockerAvailableCheck.CheckDocker(
            startProcess: () => { },
            waitForExit: _ => ++waitInvocationCount > 1,
            getExitCode: () => throw new InvalidOperationException(
                "ExitCode must not be read after a timeout."),
            killProcessTree: () => throw new InvalidOperationException(
                "The process exited before Kill ran."),
            probeTimeout: TimeSpan.FromSeconds(10),
            reapTimeout: TimeSpan.FromSeconds(2));

        check.Should().NotThrow().Which.Should().BeFalse();
        waitInvocationCount.Should().Be(2);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void Exited_probe_uses_its_exit_code(int exitCode, bool expectedAvailability)
    {
        var killInvocationCount = 0;
        var waitInvocationCount = 0;

        var isAvailable = DockerAvailableCheck.CheckDocker(
            startProcess: () => { },
            waitForExit: _ =>
            {
                waitInvocationCount++;
                return true;
            },
            getExitCode: () => exitCode,
            killProcessTree: () => killInvocationCount++,
            probeTimeout: TimeSpan.FromSeconds(10),
            reapTimeout: TimeSpan.FromSeconds(2));

        isAvailable.Should().Be(expectedAvailability);
        waitInvocationCount.Should().Be(1);
        killInvocationCount.Should().Be(0);
    }
}
