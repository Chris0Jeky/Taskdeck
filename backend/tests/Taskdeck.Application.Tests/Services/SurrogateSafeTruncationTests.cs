using FluentAssertions;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class SurrogateSafeTruncationTests
{
    [Fact]
    public void Truncate_DropsWholePairWhenBoundaryFallsBetweenCodeUnits()
    {
        var value = new string('x', 496) + "\uD83D\uDE00tail";

        var truncated = SurrogateSafeTruncation.Truncate(value, 497, "...");

        truncated.Should().Be(new string('x', 496) + "...");
        truncated.Should().NotContain("\uD83D").And.NotContain("\uDE00");
    }

    [Fact]
    public void Truncate_PreservesPairWhenBothCodeUnitsFit()
    {
        var value = new string('x', 496) + "\uD83D\uDE00tail";

        var truncated = SurrogateSafeTruncation.Truncate(value, 498, "...");

        truncated.Should().Be(new string('x', 496) + "\uD83D\uDE00...");
    }
}
