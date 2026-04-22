using FluentAssertions;
using Taskdeck.Cli.Commands;
using Xunit;

namespace Taskdeck.Cli.Tests;

public class ExitCodesTests
{
    [Fact]
    public void Success_IsZero()
    {
        ExitCodes.Success.Should().Be(0);
    }

    [Fact]
    public void Failure_IsOne()
    {
        ExitCodes.Failure.Should().Be(1);
    }

    [Fact]
    public void Usage_IsTwo()
    {
        ExitCodes.Usage.Should().Be(2);
    }

    [Fact]
    public void ExitCodes_AreDistinct()
    {
        var codes = new[] { ExitCodes.Success, ExitCodes.Failure, ExitCodes.Usage };
        codes.Should().OnlyHaveUniqueItems();
    }
}
