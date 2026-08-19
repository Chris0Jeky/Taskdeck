using System.Text.Json;
using FluentAssertions;
using Taskdeck.Application.Common;
using Taskdeck.Cli.Commands;
using Xunit;

namespace Taskdeck.Cli.Tests;

[Collection("Console Tests")]
public class VersionCommandTests
{
    [Theory]
    [InlineData("--version")]
    [InlineData("version")]
    [InlineData("--VERSION")]
    [InlineData("Version")]
    public void IsVersionRequest_RecognizesTheVersionArguments(string arg)
    {
        VersionCommand.IsVersionRequest(new[] { arg }).Should().BeTrue();
    }

    [Fact]
    public void IsVersionRequest_IgnoresTrailingArguments()
    {
        VersionCommand.IsVersionRequest(new[] { "--version", "--json" }).Should().BeTrue();
    }

    [Theory]
    [InlineData("boards")]
    [InlineData("help")]
    [InlineData("-v")]
    [InlineData("--versions")]
    public void IsVersionRequest_RejectsOtherCommands(string arg)
    {
        VersionCommand.IsVersionRequest(new[] { arg }).Should().BeFalse();
    }

    [Fact]
    public void IsVersionRequest_RejectsNoArguments()
    {
        VersionCommand.IsVersionRequest(Array.Empty<string>()).Should().BeFalse();
        VersionCommand.IsVersionRequest(null).Should().BeFalse();
    }

    [Fact]
    public void IsVersionRequest_OnlyMatchesTheFirstArgument()
    {
        VersionCommand.IsVersionRequest(new[] { "boards", "--version" }).Should().BeFalse();
    }

    [Fact]
    public void Execute_WritesTheStampedVersionAsJsonAndSucceeds()
    {
        var originalOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);
        try
        {
            var exitCode = VersionCommand.Execute();

            exitCode.Should().Be(ExitCodes.Success);

            using var document = JsonDocument.Parse(sw.ToString().Trim());
            document.RootElement.GetProperty("version").GetString()
                .Should()
                .Be(ProductVersion.Value);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}
