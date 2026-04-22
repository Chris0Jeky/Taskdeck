using System.Text.Json;
using FluentAssertions;
using Taskdeck.Cli.Commands;
using Xunit;

namespace Taskdeck.Cli.Tests;

[Collection("Console Tests")]
public class ConsoleOutputTests
{
    [Fact]
    public void JsonOptions_UsesCamelCase()
    {
        ConsoleOutput.JsonOptions.PropertyNamingPolicy.Should().Be(JsonNamingPolicy.CamelCase);
    }

    [Fact]
    public void JsonOptions_DoesNotWriteIndented()
    {
        ConsoleOutput.JsonOptions.WriteIndented.Should().BeFalse();
    }

    [Fact]
    public void PrintUsageError_ReturnsUsageExitCode()
    {
        var originalErr = Console.Error;
        using var sw = new StringWriter();
        Console.SetError(sw);
        try
        {
            var exitCode = ConsoleOutput.PrintUsageError("test message", "taskdeck test");

            exitCode.Should().Be(ExitCodes.Usage);
            sw.ToString().Should().Contain("test message");
            sw.ToString().Should().Contain("Usage: taskdeck test");
        }
        finally
        {
            Console.SetError(originalErr);
        }
    }

    [Fact]
    public void PrintFailure_ReturnsFailureExitCode()
    {
        var originalErr = Console.Error;
        using var sw = new StringWriter();
        Console.SetError(sw);
        try
        {
            var exitCode = ConsoleOutput.PrintFailure("NOT_FOUND", "Board not found");

            exitCode.Should().Be(ExitCodes.Failure);
            sw.ToString().Should().Contain("NOT_FOUND");
            sw.ToString().Should().Contain("Board not found");
        }
        finally
        {
            Console.SetError(originalErr);
        }
    }

    [Fact]
    public void WriteJson_WritesCompactCamelCaseJson()
    {
        var originalOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);
        try
        {
            ConsoleOutput.WriteJson(new { MyProperty = "hello", AnotherValue = 42 });

            var output = sw.ToString().Trim();
            output.Should().Contain("\"myProperty\"");
            output.Should().Contain("\"anotherValue\"");
            output.Should().NotContain("\n  "); // Not indented
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void WriteJson_SerializesArrays()
    {
        var originalOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);
        try
        {
            ConsoleOutput.WriteJson(new[] { new { Name = "A" }, new { Name = "B" } });

            var output = sw.ToString().Trim();
            using var doc = JsonDocument.Parse(output);
            doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
            doc.RootElement.GetArrayLength().Should().Be(2);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void PrintHelp_OutputsUsageInformation()
    {
        var originalOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);
        try
        {
            ConsoleOutput.PrintHelp();

            var output = sw.ToString();
            output.Should().Contain("Taskdeck CLI");
            output.Should().Contain("boards list");
            output.Should().Contain("boards create");
            output.Should().Contain("columns list");
            output.Should().Contain("cards add");
            output.Should().Contain("api-key create");
            output.Should().Contain("Exit codes:");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}
