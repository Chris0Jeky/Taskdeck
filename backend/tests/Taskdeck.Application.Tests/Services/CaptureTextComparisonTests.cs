using FluentAssertions;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public sealed class CaptureTextComparisonTests
{
    [Theory]
    [InlineData("a\r\nb", "a\nb", true)]
    [InlineData("a\rb", "a\nb", true)]
    [InlineData("a\nb ", "a\nb", false)]
    [InlineData("a\nb", "a\nc", false)]
    [InlineData(null, "", false)]
    [InlineData(null, null, true)]
    public void Equivalent_IgnoresOnlyLineEndingDifferences(string? source, string? queueText, bool expected)
    {
        CaptureTextComparison.Equivalent(source, queueText).Should().Be(expected);
    }
}
