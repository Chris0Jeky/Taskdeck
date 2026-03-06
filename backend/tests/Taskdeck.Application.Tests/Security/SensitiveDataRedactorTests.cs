using FluentAssertions;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Security;

public class SensitiveDataRedactorTests
{
    [Fact]
    public void Redact_ShouldMaskAuthorizationHeadersSecretsAndCaptureText()
    {
        var raw = """
                  Authorization: Bearer super-secret-token
                  x-goog-api-key: gemini-secret
                  token=queue-secret&password=dev-secret
                  {"text":"capture secret note","titleHint":"private title","externalRef":"https://example.com/private"}
                  """;

        var redacted = SensitiveDataRedactor.Redact(raw);

        redacted.Should().NotContain("super-secret-token");
        redacted.Should().NotContain("gemini-secret");
        redacted.Should().NotContain("queue-secret");
        redacted.Should().NotContain("dev-secret");
        redacted.Should().NotContain("capture secret note");
        redacted.Should().NotContain("private title");
        redacted.Should().NotContain("https://example.com/private");
        redacted.Should().Contain($"Authorization: Bearer {SensitiveDataRedactor.RedactedValue}");
        redacted.Should().Contain($"x-goog-api-key: {SensitiveDataRedactor.RedactedValue}");
    }

    [Fact]
    public void SummarizeException_ShouldRedactSensitiveInnerExceptionMessages()
    {
        var exception = new InvalidOperationException(
            "Authorization: Bearer outer-secret",
            new HttpRequestException("{\"text\":\"inner capture secret\"}"));

        var summary = SensitiveDataRedactor.SummarizeException(exception);

        summary.Should().Contain("InvalidOperationException");
        summary.Should().Contain("HttpRequestException");
        summary.Should().NotContain("outer-secret");
        summary.Should().NotContain("inner capture secret");
        summary.Should().Contain($"Authorization: Bearer {SensitiveDataRedactor.RedactedValue}");
    }

    [Fact]
    public void Redact_ShouldMaskEntireJsonStringValues_WhenSensitiveFieldsContainEscapedQuotes()
    {
        var raw =
            "{\"text\":\"secret \\\"quoted\\\" content\",\"payload\":\"apiKey \\\"nested\\\" value\",\"token\":\"secret-token\"}";

        var redacted = SensitiveDataRedactor.Redact(raw);

        redacted.Should().Contain($"\"text\":\"{SensitiveDataRedactor.RedactedValue}\"");
        redacted.Should().Contain($"\"payload\":\"{SensitiveDataRedactor.RedactedValue}\"");
        redacted.Should().Contain($"\"token\":\"{SensitiveDataRedactor.RedactedValue}\"");
        redacted.Should().NotContain("secret \\\"quoted\\\" content");
        redacted.Should().NotContain("apiKey \\\"nested\\\" value");
        redacted.Should().NotContain("secret-token");
    }

    [Fact]
    public void SummarizeException_ShouldLimitDepthAndLength()
    {
        var deepest = new InvalidOperationException(new string('x', 2_000));
        var fourth = new InvalidOperationException("fourth", deepest);
        var third = new InvalidOperationException("third", fourth);
        var second = new InvalidOperationException("second", third);
        var first = new InvalidOperationException("first", second);
        var root = new InvalidOperationException("root", first);

        var summary = SensitiveDataRedactor.SummarizeException(root);

        summary.Should().Contain("InvalidOperationException: root");
        summary.Should().Contain("additional inner exceptions truncated after");
        summary.Length.Should().BeLessThanOrEqualTo(1_039); // cap + suffix length
        summary.Should().NotContain(new string('x', 100));
    }

    [Fact]
    public void SummarizeException_ShouldNotReportTruncation_WhenChainFitsDepthLimit()
    {
        var fourth = new InvalidOperationException("fourth");
        var third = new InvalidOperationException("third", fourth);
        var second = new InvalidOperationException("second", third);
        var first = new InvalidOperationException("first", second);
        var root = new InvalidOperationException("root", first);

        var summary = SensitiveDataRedactor.SummarizeException(root);

        summary.Should().Contain("InvalidOperationException: root");
        summary.Should().Contain("InvalidOperationException: fourth");
        summary.Should().NotContain("additional inner exceptions truncated after");
    }
}
