using FluentAssertions;
using Taskdeck.Cli.Commands;
using Xunit;

namespace Taskdeck.Cli.Tests;

public class ArgParserTests
{
    [Fact]
    public void HasFlag_WhenFlagPresent_ReturnsTrue()
    {
        var args = new[] { "--board", "abc", "--json" };

        ArgParser.HasFlag(args, "--json").Should().BeTrue();
    }

    [Fact]
    public void HasFlag_WhenFlagAbsent_ReturnsFalse()
    {
        var args = new[] { "--board", "abc" };

        ArgParser.HasFlag(args, "--json").Should().BeFalse();
    }

    [Fact]
    public void HasFlag_IsCaseInsensitive()
    {
        var args = new[] { "--JSON" };

        ArgParser.HasFlag(args, "--json").Should().BeTrue();
    }

    [Fact]
    public void HasFlag_EmptyArgs_ReturnsFalse()
    {
        var args = Array.Empty<string>();

        ArgParser.HasFlag(args, "--json").Should().BeFalse();
    }

    [Fact]
    public void GetOption_ReturnsValueAfterOptionName()
    {
        var args = new[] { "--board", "abc-123", "--name", "Test" };

        ArgParser.GetOption(args, "--board").Should().Be("abc-123");
        ArgParser.GetOption(args, "--name").Should().Be("Test");
    }

    [Fact]
    public void GetOption_WhenOptionAbsent_ReturnsNull()
    {
        var args = new[] { "--board", "abc-123" };

        ArgParser.GetOption(args, "--name").Should().BeNull();
    }

    [Fact]
    public void GetOption_WhenOptionIsLastArg_ReturnsNull()
    {
        // If --board is the last argument, there's no value after it
        var args = new[] { "--board" };

        ArgParser.GetOption(args, "--board").Should().BeNull();
    }

    [Fact]
    public void GetOption_IsCaseInsensitive()
    {
        var args = new[] { "--BOARD", "abc-123" };

        ArgParser.GetOption(args, "--board").Should().Be("abc-123");
    }

    [Fact]
    public void GetOption_EmptyArgs_ReturnsNull()
    {
        var args = Array.Empty<string>();

        ArgParser.GetOption(args, "--board").Should().BeNull();
    }

    [Theory]
    [InlineData("d1a7b8c0-1234-5678-9abc-def012345678", true)]
    [InlineData("D1A7B8C0-1234-5678-9ABC-DEF012345678", true)]
    [InlineData("not-a-guid", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryParseGuid_ValidatesCorrectly(string? text, bool expectedResult)
    {
        var result = ArgParser.TryParseGuid(text, out var value);

        result.Should().Be(expectedResult);
        if (expectedResult)
        {
            value.Should().NotBe(Guid.Empty);
        }
    }

    [Fact]
    public void StripFlag_RemovesFlagFromArgs()
    {
        var args = new[] { "--board", "abc", "--json", "--name", "Test" };

        var result = ArgParser.StripFlag(args, "--json");

        result.Should().Equal("--board", "abc", "--name", "Test");
    }

    [Fact]
    public void StripFlag_WhenFlagAbsent_ReturnsAllArgs()
    {
        var args = new[] { "--board", "abc" };

        var result = ArgParser.StripFlag(args, "--json");

        result.Should().Equal("--board", "abc");
    }

    [Fact]
    public void StripFlag_IsCaseInsensitive()
    {
        var args = new[] { "--JSON", "--board", "abc" };

        var result = ArgParser.StripFlag(args, "--json");

        result.Should().Equal("--board", "abc");
    }

    [Fact]
    public void StripFlag_EmptyArgs_ReturnsEmpty()
    {
        var args = Array.Empty<string>();

        var result = ArgParser.StripFlag(args, "--json");

        result.Should().BeEmpty();
    }

    [Fact]
    public void StripFlag_MultipleOccurrences_RemovesAll()
    {
        var args = new[] { "--json", "--board", "abc", "--json" };

        var result = ArgParser.StripFlag(args, "--json");

        result.Should().Equal("--board", "abc");
    }
}
