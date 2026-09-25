using FluentAssertions;
using Taskdeck.Cli.Commands;
using Xunit;

namespace Taskdeck.Cli.Tests;

public class ArgParserMissingValueTests
{
    [Fact]
    public void GetOption_WhenNextTokenIsAnotherOption_ReturnsNull()
    {
        var args = new[] { "--name", "--position", "0" };

        ArgParser.GetOption(args, "--name").Should().BeNull();
    }

    [Fact]
    public void GetOption_WhenNextTokenStartsWithDashes_DoesNotConsumeIt()
    {
        var args = new[] { "--board", "abc", "--name", "--json" };

        ArgParser.GetOption(args, "--name").Should().BeNull();
        ArgParser.HasFlag(args, "--name").Should().BeTrue();
    }

    [Fact]
    public void GetOption_WhenValueIsOrdinaryText_StillReturnsValue()
    {
        var args = new[] { "--name", "Todo", "--position", "0" };

        ArgParser.GetOption(args, "--name").Should().Be("Todo");
        ArgParser.GetOption(args, "--position").Should().Be("0");
    }
}
