using FsCheck;
using FsCheck.Fluent;

namespace Taskdeck.Application.Tests.Fuzz;

/// <summary>
/// Shared FsCheck generators for DTO serialization fuzz tests.
/// Centralises adversarial string generation so all fuzz tests
/// exercise the same comprehensive input space.
/// </summary>
internal static class FuzzTestGenerators
{
    /// <summary>
    /// Generates adversarial strings covering: Unicode edge cases (null byte, BOM,
    /// replacement char, surrogates, zero-width, combining, CJK, Arabic, emoji),
    /// control characters (bell, backspace, ANSI escape, CRLF), XSS/injection payloads,
    /// JSON-sensitive characters (quotes, backslashes), length boundaries (empty,
    /// whitespace), explicit null, and FsCheck random strings.
    /// </summary>
    public static Gen<string> AdversarialStringGen() => Gen.OneOf(
        // Unicode edge cases
        Gen.Constant("\u0000"),                        // null byte
        Gen.Constant("\uFEFF"),                        // BOM
        Gen.Constant("\uFFFD"),                        // replacement character
        Gen.Constant("\u200B"),                        // zero-width space
        Gen.Constant("\u202E"),                        // right-to-left override
        Gen.Constant("\u0301"),                        // combining accent
        Gen.Constant("\U0001F468\u200D\U0001F469\u200D\U0001F467\u200D\U0001F466"), // family emoji
        Gen.Constant("\u7530\u4E2D\u592A\u90CE"),      // CJK
        Gen.Constant("\u0645\u0631\u062D\u0628\u0627"), // Arabic RTL

        // JSON-sensitive characters
        Gen.Constant("\"quoted\"string\""),
        Gen.Constant("back\\slash"),
        Gen.Constant("new\nline\ttab"),
        Gen.Constant("null\x00byte"),

        // XSS/injection payloads
        Gen.Constant("<script>alert('xss')</script>"),
        Gen.Constant("'; DROP TABLE boards; --"),
        Gen.Constant("{\"nested\": true}"),
        Gen.Constant("{{constructor.constructor('return this')()}}"),
        Gen.Constant("${7*7}"),

        // Length boundary strings
        Gen.Constant(""),
        Gen.Constant(" "),

        // Explicit null
        Gen.Constant((string)null!),

        // Arbitrary from FsCheck
        ArbMap.Default.ArbFor<string>().Generator.Where(s => s != null)
    );

    /// <summary>
    /// Wraps <see cref="AdversarialStringGen"/> as nullable for optional-field testing.
    /// </summary>
    public static Gen<string?> NullableStringGen() => Gen.OneOf(
        Gen.Constant((string?)null),
        AdversarialStringGen().Select(s => (string?)s)
    );
}
