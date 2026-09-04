using FluentAssertions;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class LogControlCharacterSanitizerTests
{
    [Fact]
    public void Strip_ShouldRemoveAllC0DelAndC1ControlsWhilePreservingPrintableUnicode()
    {
        const string printable = "ordinary café ✓ emoji 😀";
        var controls = string.Concat(Enumerable.Range('\u0000', 0x20).Select(static value => (char)value))
            + '\u007F'
            + string.Concat(Enumerable.Range('\u0080', 0x20).Select(static value => (char)value));

        var sanitized = LogControlCharacterSanitizer.Strip($"{printable}{controls}{printable}");

        sanitized.Should().Be($"{printable}{printable}");
    }

    [Fact]
    public void Strip_ShouldRemoveUnicodeRecordSeparators()
    {
        const string value = "before\u0085next-line\u2028line\u2029paragraph after";

        var sanitized = LogControlCharacterSanitizer.Strip(value);

        sanitized.Should().Be("beforenext-linelineparagraph after");
    }

    [Fact]
    public void LogValueSanitizer_ShouldNotSplitSurrogatePairWhenControlsShiftBoundary()
    {
        var value = new string('x', 199) + "\u001B😀tail";

        var sanitized = LogValueSanitizer.Sanitize(value);

        sanitized.Should().Be(new string('x', 199) + "...");
        sanitized.Should().NotContain("\uD83D").And.NotContain("\uDE00");
    }

    [Fact]
    public void Strip_ShouldRemoveUnpairedSurrogates()
    {
        const string value = "before\uD83Dmiddle\uDE00after";

        var sanitized = LogControlCharacterSanitizer.Strip(value);

        sanitized.Should().Be("beforemiddleafter");
    }
}
