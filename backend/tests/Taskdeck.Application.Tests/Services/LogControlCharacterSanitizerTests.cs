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

    [Fact]
    public void Strip_ShouldRemoveInvisibleUnicodeFormatCharacters()
    {
        // General category Cf: the zero-width joiners and space, the bidirectional marks and
        // overrides, the word-joiner block, the byte-order mark and the soft hyphen. They render as
        // nothing while hiding or reordering the text around them, so a caller-controlled value
        // carrying them can make a log line read as something it is not (#2519).
        const string formats =
            "\u200B\u200C\u200D\u200E\u200F\u202A\u202B\u202C\u202D\u202E"
            + "\u2060\u2061\u2062\u2063\u2064\uFEFF\u00AD";

        var sanitized = LogControlCharacterSanitizer.Strip($"before{formats}after");

        sanitized.Should().Be("beforeafter");
    }

    [Fact]
    public void Strip_ShouldKeepPrintablePunctuationAndSymbolsAroundStrippedFormatCharacters()
    {
        // The Cf rule must not widen into ordinary punctuation, marks or symbols.
        const string value = "zero\u200Bwidth \u2014 dash, \u2713 tick, caf\u00E9";

        var sanitized = LogControlCharacterSanitizer.Strip(value);

        sanitized.Should().Be("zerowidth \u2014 dash, \u2713 tick, caf\u00E9");
    }
}
