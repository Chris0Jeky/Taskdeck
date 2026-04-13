using FsCheck;
using FsCheck.Fluent;

namespace Taskdeck.Domain.Tests.PropertyBased;

/// <summary>
/// Shared FsCheck generators for property-based domain tests.
/// Centralises adversarial string generation so all entity tests
/// exercise the same comprehensive input space.
/// </summary>
internal static class TestGenerators
{
    /// <summary>
    /// Generates adversarial strings covering: Unicode edge cases (null byte, BOM,
    /// surrogates, zero-width, combining, CJK, Arabic, emoji), control characters
    /// (bell, backspace, ANSI escape, CRLF), XSS/injection payloads (script tags,
    /// SQL injection, prototype pollution, URI schemes), length boundaries (empty,
    /// whitespace), explicit null, and FsCheck random strings.
    /// </summary>
    public static Gen<string> AdversarialStringGen() => Gen.OneOf(
        // Unicode edge cases
        Gen.Constant("\u0000"),                        // null byte
        Gen.Constant("\uFEFF"),                        // BOM
        Gen.Constant("\uFFFD"),                        // replacement character
        Gen.Constant("\uD800"),                        // lone high surrogate (invalid)
        Gen.Constant("\uDBFF\uDFFF"),                  // max surrogate pair
        Gen.Constant("\u200B"),                        // zero-width space
        Gen.Constant("\u200E"),                        // left-to-right mark
        Gen.Constant("\u202E"),                        // right-to-left override
        Gen.Constant("\u0301"),                        // combining accent
        Gen.Constant("\u00E9"),                        // precomposed e-acute
        Gen.Constant("e\u0301"),                       // decomposed equivalent
        Gen.Constant("\U0001F468\u200D\U0001F469\u200D\U0001F467\u200D\U0001F466"), // family emoji
        Gen.Constant("\U0001D54B\U0001D564\U0001D564\U0001D565"), // math bold symbols
        Gen.Constant("\u7530\u4E2D\u592A\u90CE"),      // CJK
        Gen.Constant("\u0645\u0631\u062D\u0628\u0627"), // Arabic RTL
        Gen.Constant("\u0E01\u0E38"),                   // Thai combining

        // Control characters
        Gen.Constant("\x01\x02\x03"),                  // ASCII control chars
        Gen.Constant("\x07"),                           // bell
        Gen.Constant("\x08"),                           // backspace
        Gen.Constant("\x1B[31m"),                       // ANSI escape
        Gen.Constant("\r\n\r\n"),                       // CRLF
        Gen.Constant("\t\t\t"),                         // tabs

        // XSS/injection payloads
        Gen.Constant("<script>alert('xss')</script>"),
        Gen.Constant("'; DROP TABLE boards; --"),
        Gen.Constant("\" OR 1=1 --"),
        Gen.Constant("<img src=x onerror=alert(1)>"),
        Gen.Constant("{{constructor.constructor('return this')()}}"),
        Gen.Constant("javascript:alert(1)"),
        Gen.Constant("data:text/html,<script>alert(1)</script>"),
        Gen.Constant("${7*7}"),                        // template injection
        Gen.Constant("#{7*7}"),                        // template injection

        // URI scheme attacks (relevant for webhook URLs, stored URLs)
        Gen.Constant("file:///etc/passwd"),
        Gen.Constant("http://169.254.169.254/"),       // SSRF

        // Length boundary strings
        Gen.Constant(""),                              // empty
        Gen.Constant(" "),                             // single space
        Gen.Constant(new string('\t', 50)),            // many tabs
        Gen.Constant(new string('\n', 50)),            // many newlines

        // Explicit null
        Gen.Constant((string)null!),

        // Arbitrary from FsCheck (filter nulls -- null is already covered above)
        ArbMap.Default.ArbFor<string>().Generator.Where(s => s != null)
    );

    /// <summary>
    /// Wraps <see cref="AdversarialStringGen"/> as nullable for optional-field testing.
    /// </summary>
    public static Gen<string?> NullableAdversarialStringGen() => Gen.OneOf(
        Gen.Constant((string?)null),
        AdversarialStringGen().Select(s => (string?)s)
    );
}
