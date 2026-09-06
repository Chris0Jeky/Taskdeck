using FluentAssertions;
using Taskdeck.Api.Telemetry;
using Xunit;

namespace Taskdeck.Api.Tests.Telemetry;

public class LogSanitizerTests
{
    [Fact]
    public void SanitizeForLog_ShouldNotSplitSurrogatePairWhenControlsShiftBoundary()
    {
        var value = new string('x', 199) + "\u001B😀tail";

        var sanitized = LogSanitizer.SanitizeForLog(value);

        sanitized.Should().Be(new string('x', 199) + "...");
        sanitized.Should().NotContain("\uD83D").And.NotContain("\uDE00");
    }
}
