using FluentAssertions;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class TelemetryGuardTests : IDisposable
{
    public TelemetryGuardTests()
    {
        // Reset to defaults before each test to avoid cross-test pollution
        TelemetryGuard.ResetToDefaults();
    }

    public void Dispose()
    {
        TelemetryGuard.ResetToDefaults();
    }

    // --- Allowlist validation ---

    [Fact]
    public void Validate_ShouldAccept_AllowlistedKey()
    {
        var result = TelemetryGuard.Validate("capture.count", 42);
        result.IsValid.Should().BeTrue();
        result.Reason.Should().BeNull();
    }

    [Fact]
    public void Validate_ShouldReject_UnknownKey()
    {
        var result = TelemetryGuard.Validate("user.email_address", "test");
        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("not in the allowlist");
    }

    [Fact]
    public void Validate_ShouldReject_EmptyKey()
    {
        var result = TelemetryGuard.Validate("", 1);
        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("null or empty");
    }

    [Fact]
    public void Validate_ShouldReject_WhitespaceKey()
    {
        var result = TelemetryGuard.Validate("   ", 1);
        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("null or empty");
    }

    // --- Null value rejection ---

    [Fact]
    public void Validate_ShouldReject_NullValue()
    {
        var result = TelemetryGuard.Validate("capture.count", null);
        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("null");
    }

    // --- String length validation ---

    [Fact]
    public void Validate_ShouldAccept_ShortString()
    {
        var result = TelemetryGuard.Validate("workspace.mode", "guided");
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ShouldReject_LongString()
    {
        var longString = new string('a', 257);
        var result = TelemetryGuard.Validate("workspace.mode", longString);
        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("maximum length");
    }

    [Fact]
    public void Validate_ShouldAccept_StringAtExactMaxLength()
    {
        var exactString = new string('a', 256);
        var result = TelemetryGuard.Validate("workspace.mode", exactString);
        result.IsValid.Should().BeTrue();
    }

    // --- URL rejection ---

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://malicious-site.org/path")]
    [InlineData("Check https://evil.com for details")]
    [InlineData("ftp://files.example.com/data")]
    public void Validate_ShouldReject_StringsContainingUrls(string value)
    {
        var result = TelemetryGuard.Validate("workspace.mode", value);
        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("URL");
    }

    [Theory]
    [InlineData("no-url-here")]
    [InlineData("path/to/file")]
    [InlineData("protocol without colon-slash")]
    public void Validate_ShouldAccept_StringsWithoutUrls(string value)
    {
        var result = TelemetryGuard.Validate("workspace.mode", value);
        result.IsValid.Should().BeTrue();
    }

    // --- Email rejection ---

    [Theory]
    [InlineData("user@example.com")]
    [InlineData("admin@company.org")]
    [InlineData("Contact alice@domain.co.uk for help")]
    public void Validate_ShouldReject_StringsContainingEmails(string value)
    {
        var result = TelemetryGuard.Validate("workspace.mode", value);
        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("email");
    }

    [Theory]
    [InlineData("no-email")]
    [InlineData("at-sign-alone@")]
    [InlineData("@no-local-part.com")]
    public void Validate_ShouldAccept_StringsWithoutEmails(string value)
    {
        var result = TelemetryGuard.Validate("workspace.mode", value);
        result.IsValid.Should().BeTrue();
    }

    // --- Non-finite double rejection ---

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Validate_ShouldReject_NonFiniteDoubles(double value)
    {
        var result = TelemetryGuard.Validate("llm.latency_ms", value);
        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("finite");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(42.5)]
    [InlineData(-100.0)]
    [InlineData(double.MaxValue)]
    [InlineData(double.MinValue)]
    public void Validate_ShouldAccept_FiniteDoubles(double value)
    {
        var result = TelemetryGuard.Validate("llm.latency_ms", value);
        result.IsValid.Should().BeTrue();
    }

    // --- Non-finite float rejection ---

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void Validate_ShouldReject_NonFiniteFloats(float value)
    {
        var result = TelemetryGuard.Validate("llm.latency_ms", value);
        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("finite");
    }

    // --- Integer values ---

    [Fact]
    public void Validate_ShouldAccept_IntegerValues()
    {
        var result = TelemetryGuard.Validate("capture.count", 42);
        result.IsValid.Should().BeTrue();
    }

    // --- Custom options ---

    [Fact]
    public void Configure_ShouldOverrideDefaults()
    {
        var customOptions = new TelemetryGuardOptions
        {
            MaxStringLength = 10,
            AllowedKeys = new HashSet<string>(StringComparer.Ordinal) { "custom.key" },
        };
        TelemetryGuard.Configure(customOptions);

        TelemetryGuard.Validate("custom.key", "short").IsValid.Should().BeTrue();
        TelemetryGuard.Validate("custom.key", "this-is-too-long").IsValid.Should().BeFalse();
        TelemetryGuard.Validate("capture.count", 1).IsValid.Should().BeFalse(); // default key no longer allowed
    }

    [Fact]
    public void Configure_ShouldThrow_ForNullOptions()
    {
        var act = () => TelemetryGuard.Configure(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // --- Adversarial / fuzz inputs ---

    [Theory]
    [InlineData("capture.count", "normal text")]  // wrong type but allowed key, accepted since string is clean
    [InlineData("llm.request_count", 0)]
    [InlineData("llm.token_input_count", int.MaxValue)]
    public void Validate_ShouldAccept_EdgeCaseValidInputs(string key, object value)
    {
        var result = TelemetryGuard.Validate(key, value);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ShouldReject_StringWithBothUrlAndEmail()
    {
        var result = TelemetryGuard.Validate("workspace.mode", "Visit https://evil.com or email user@bad.com");
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ShouldHandle_UnicodeStringSafely()
    {
        var result = TelemetryGuard.Validate("workspace.mode", "日本語テスト");
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ShouldHandle_EmptyString()
    {
        var result = TelemetryGuard.Validate("workspace.mode", "");
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ShouldReject_ReDoSPatternInUrl()
    {
        // Adversarial string designed to cause catastrophic backtracking in naive URL regex.
        // With our compiled regex + timeout, this should either match quickly or timeout safely.
        var adversarial = "http://" + new string('a', 50) + "." + new string('a', 50);
        var result = TelemetryGuard.Validate("workspace.mode", adversarial);
        // The important thing is this completes in reasonable time, not whether it matches.
        // It contains http:// so it should be rejected as a URL.
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ShouldReject_ReDoSPatternInEmail()
    {
        // Adversarial string designed to stress email regex.
        var adversarial = new string('a', 50) + "@" + new string('b', 50) + ".com";
        var result = TelemetryGuard.Validate("workspace.mode", adversarial);
        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("email");
    }

    [Theory]
    [InlineData("capture.count")]
    [InlineData("proposal.generated_count")]
    [InlineData("board.card_count")]
    [InlineData("session.duration_ms")]
    [InlineData("llm.request_count")]
    [InlineData("automation.run_count")]
    [InlineData("workspace.mode")]
    public void Validate_ShouldAccept_AllDefaultAllowlistedKeys(string key)
    {
        var result = TelemetryGuard.Validate(key, 1);
        result.IsValid.Should().BeTrue();
    }

    // --- Unsupported value type rejection ---

    [Fact]
    public void Validate_ShouldReject_DictionaryValue()
    {
        var dict = new Dictionary<string, string> { { "user", "alice@example.com" } };
        var result = TelemetryGuard.Validate("capture.count", dict);
        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("not supported");
    }

    [Fact]
    public void Validate_ShouldReject_ObjectValue()
    {
        var obj = new { Name = "secret", Email = "user@evil.com" };
        var result = TelemetryGuard.Validate("capture.count", obj);
        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("not supported");
    }

    [Fact]
    public void Validate_ShouldReject_ArrayValue()
    {
        var arr = new[] { "one", "two", "three" };
        var result = TelemetryGuard.Validate("capture.count", arr);
        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("not supported");
    }

    [Fact]
    public void Validate_ShouldReject_ListValue()
    {
        var list = new List<int> { 1, 2, 3 };
        var result = TelemetryGuard.Validate("capture.count", list);
        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("not supported");
    }

    [Fact]
    public void Validate_ShouldAccept_BoolValue()
    {
        var result = TelemetryGuard.Validate("capture.count", true);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ShouldAccept_LongValue()
    {
        var result = TelemetryGuard.Validate("capture.count", 123L);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ShouldAccept_DecimalValue()
    {
        var result = TelemetryGuard.Validate("capture.count", 99.9m);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ShouldReject_DateTimeValue()
    {
        var result = TelemetryGuard.Validate("capture.count", DateTime.UtcNow);
        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("not supported");
    }

    [Fact]
    public void Validate_ShouldReject_GuidValue()
    {
        var result = TelemetryGuard.Validate("capture.count", Guid.NewGuid());
        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("not supported");
    }

    // --- Encoded string bypass prevention ---

    [Fact]
    public void Validate_ShouldReject_UrlEncodedEmail()
    {
        // %40 is URL-encoded '@' -- must still be caught
        var result = TelemetryGuard.Validate("workspace.mode", "user%40example.com");
        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("email");
    }

    [Fact]
    public void Validate_ShouldReject_UrlEncodedUrl()
    {
        // %3A is URL-encoded ':' and %2F is '/'
        var result = TelemetryGuard.Validate("workspace.mode", "https%3A%2F%2Fevil.com");
        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("URL");
    }

    [Fact]
    public void Validate_ShouldReject_HtmlEncodedEmail()
    {
        // &#64; is HTML-encoded '@'
        var result = TelemetryGuard.Validate("workspace.mode", "user&#64;example.com");
        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("email");
    }

    [Fact]
    public void Validate_ShouldReject_HtmlEncodedUrl()
    {
        // &#58; is HTML-encoded ':'
        var result = TelemetryGuard.Validate("workspace.mode", "https&#58;//evil.com");
        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("URL");
    }

    [Fact]
    public void Validate_ShouldReject_DoubleUrlEncodedUrl()
    {
        var result = TelemetryGuard.Validate("workspace.mode", "https%253A%252F%252Fevil.com");
        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("URL");
    }

    [Fact]
    public void Validate_ShouldReject_MixedUrlThenHtmlEncodedEmail()
    {
        var result = TelemetryGuard.Validate("workspace.mode", "user%26%2364%3Bexample.com");
        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("email");
    }

    [Fact]
    public void Validate_ShouldReject_RepeatedHtmlEncodedEmail()
    {
        var result = TelemetryGuard.Validate("workspace.mode", "user&amp;#64;example.com");
        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("email");
    }

    [Fact]
    public void Validate_ShouldAccept_PercentLiteralThatIsNotEncoding()
    {
        // A string with % that is NOT valid URL encoding should still pass if clean
        var result = TelemetryGuard.Validate("workspace.mode", "100% complete");
        result.IsValid.Should().BeTrue();
    }
}
