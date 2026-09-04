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
}
