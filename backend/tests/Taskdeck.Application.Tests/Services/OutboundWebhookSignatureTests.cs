using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class OutboundWebhookSignatureTests
{
    [Fact]
    public void Compute_ShouldBeDeterministic_ForSameInputs()
    {
        var timestamp = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var payload = "{\"event\":\"card.updated\"}";

        var first = OutboundWebhookSignature.Compute("test-secret", timestamp, payload);
        var second = OutboundWebhookSignature.Compute("test-secret", timestamp, payload);

        first.Should().Be(second);
        first.Should().MatchRegex("^[a-f0-9]{64}$");
    }

    [Fact]
    public void Compute_ShouldProduceExpectedHmac_ForKnownInput()
    {
        var signingSecret = "known-secret";
        var timestamp = DateTimeOffset.FromUnixTimeSeconds(1_700_000_123);
        var payload = "{\"event\":\"board.updated\",\"id\":\"123\"}";
        var expected = ComputeExpectedSignature(signingSecret, timestamp, payload);

        var actual = OutboundWebhookSignature.Compute(signingSecret, timestamp, payload);

        actual.Should().Be(expected);
    }

    [Fact]
    public void Compute_ShouldMatchExpectedSignature_WhenInputsAreCorrect()
    {
        var signingSecret = "test-secret";
        var timestamp = DateTimeOffset.FromUnixTimeSeconds(1_700_000_456);
        var payload = "{\"event\":\"card.created\",\"id\":\"42\"}";
        var expectedSignature = OutboundWebhookSignature.Compute(signingSecret, timestamp, payload);

        var recomputedSignature = OutboundWebhookSignature.Compute(signingSecret, timestamp, payload);

        recomputedSignature.Should().Be(expectedSignature);
    }

    [Fact]
    public void Compute_ShouldProduceDifferentSignature_WhenKeyIsWrong()
    {
        var timestamp = DateTimeOffset.FromUnixTimeSeconds(1_700_000_456);
        var payload = "{\"event\":\"card.created\",\"id\":\"42\"}";
        var expectedSignature = OutboundWebhookSignature.Compute("correct-secret", timestamp, payload);

        var wrongKeySignature = OutboundWebhookSignature.Compute("wrong-secret", timestamp, payload);

        wrongKeySignature.Should().NotBe(expectedSignature);
    }

    [Fact]
    public void Compute_ShouldProduceDifferentSignature_WhenPayloadIsTampered()
    {
        var signingSecret = "test-secret";
        var timestamp = DateTimeOffset.FromUnixTimeSeconds(1_700_000_456);
        var payload = "{\"event\":\"card.created\",\"id\":\"42\"}";
        var expectedSignature = OutboundWebhookSignature.Compute(signingSecret, timestamp, payload);

        var tamperedPayloadSignature = OutboundWebhookSignature.Compute(signingSecret, timestamp, "{\"event\":\"card.created\",\"id\":\"43\"}");

        tamperedPayloadSignature.Should().NotBe(expectedSignature);
    }

    [Fact]
    public void Compute_ShouldReturnValidSignature_ForEmptyPayload()
    {
        var signature = OutboundWebhookSignature.Compute(
            "test-secret",
            DateTimeOffset.FromUnixTimeSeconds(1_700_000_789),
            string.Empty);

        signature.Should().NotBeNullOrWhiteSpace();
        signature.Should().MatchRegex("^[a-f0-9]{64}$");
    }

    [Fact]
    public void Compute_ShouldThrow_WhenSigningSecretIsNull()
    {
        var act = () => OutboundWebhookSignature.Compute(
            null!,
            DateTimeOffset.FromUnixTimeSeconds(1_700_000_789),
            "{}");

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Compute_ShouldReturnValidSignature_WhenSigningSecretIsEmpty()
    {
        var signature = OutboundWebhookSignature.Compute(
            string.Empty,
            DateTimeOffset.FromUnixTimeSeconds(1_700_000_789),
            "{}");

        signature.Should().NotBeNullOrWhiteSpace();
        signature.Should().MatchRegex("^[a-f0-9]{64}$");
    }

    [Fact]
    public void Compute_ShouldHandleLargePayload_WithoutError()
    {
        var payload = new string('a', 100_000);

        var signature = OutboundWebhookSignature.Compute(
            "test-secret",
            DateTimeOffset.FromUnixTimeSeconds(1_700_000_999),
            payload);

        signature.Should().NotBeNullOrWhiteSpace();
        signature.Should().MatchRegex("^[a-f0-9]{64}$");
    }

    private static string ComputeExpectedSignature(string signingSecret, DateTimeOffset timestamp, string payload)
    {
        var canonical = $"{timestamp.ToUnixTimeSeconds()}.{payload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
