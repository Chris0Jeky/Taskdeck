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
}
