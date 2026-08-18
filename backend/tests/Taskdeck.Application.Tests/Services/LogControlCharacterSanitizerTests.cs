using FluentAssertions;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class LogControlCharacterSanitizerTests
{
    [Fact]
    public void Strip_ShouldRemoveAllC0DelAndC1ControlsWhilePreservingPrintableUnicode()
    {
        const string printable = "ordinary café ✓ \u2028line separator\u2029paragraph separator";
        var controls = string.Concat(Enumerable.Range('\u0000', 0x20).Select(static value => (char)value))
            + '\u007F'
            + string.Concat(Enumerable.Range('\u0080', 0x20).Select(static value => (char)value));

        var sanitized = LogControlCharacterSanitizer.Strip($"{printable}{controls}{printable}");

        sanitized.Should().Be($"{printable}{printable}");
    }
}
