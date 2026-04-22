using FluentAssertions;
using Xunit;

namespace Taskdeck.Cli.Tests;

public class CommandDispatcherTests
{
    [Fact]
    public async Task NoArgs_PrintsHelpAndReturnsUsageExitCode()
    {
        await using var harness = new CliTestHarness("cli-dispatch");

        var result = await harness.RunAsync("");

        result.ExitCode.Should().Be(2);
        result.StdOut.Should().Contain("Taskdeck CLI");
        result.StdOut.Should().Contain("Usage:");
    }

    [Fact]
    public async Task Help_PrintsHelpAndReturnsSuccess()
    {
        await using var harness = new CliTestHarness("cli-dispatch");

        var result = await harness.RunAsync("help");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("Taskdeck CLI");
        result.StdOut.Should().Contain("boards");
        result.StdOut.Should().Contain("columns");
        result.StdOut.Should().Contain("cards");
        result.StdOut.Should().Contain("api-key");
    }

    [Fact]
    public async Task UnknownCommandGroup_ReturnsUsageError()
    {
        await using var harness = new CliTestHarness("cli-dispatch");

        var result = await harness.RunAsync("nonexistent");

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("Unknown command group");
    }
}
