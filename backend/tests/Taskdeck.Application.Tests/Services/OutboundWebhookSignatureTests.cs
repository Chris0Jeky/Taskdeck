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
        // Expected value computed independently via Python:
        // hmac.new(b'known-secret', b'1700000123.{"event":"board.updated","id":"123"}', hashlib.sha256).hexdigest()
        const string expected = "c94507c8910f1ea62c6c240331acdacbe66291bab0d4bb733d6ed4cae3b27a2b";

        var actual = OutboundWebhookSignature.Compute(
            "known-secret",
            DateTimeOffset.FromUnixTimeSeconds(1_700_000_123),
            "{\"event\":\"board.updated\",\"id\":\"123\"}");

        actual.Should().Be(expected);
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
        var original = OutboundWebhookSignature.Compute(signingSecret, timestamp, "{\"id\":\"42\"}");

        var tampered = OutboundWebhookSignature.Compute(signingSecret, timestamp, "{\"id\":\"43\"}");

        tampered.Should().NotBe(original);
    }

    [Fact]
    public void Compute_ShouldProduceDifferentSignature_WhenTimestampChanges()
    {
        var signingSecret = "test-secret";
        var payload = "{\"event\":\"card.updated\"}";
        var sig1 = OutboundWebhookSignature.Compute(signingSecret, DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), payload);

        var sig2 = OutboundWebhookSignature.Compute(signingSecret, DateTimeOffset.FromUnixTimeSeconds(1_700_000_001), payload);

        sig2.Should().NotBe(sig1);
    }

    [Fact]
    public void Compute_ShouldReturnValidSignature_ForEmptyPayload()
    {
        var emptyPayloadSig = OutboundWebhookSignature.Compute(
            "test-secret",
            DateTimeOffset.FromUnixTimeSeconds(1_700_000_789),
            string.Empty);

        var normalSig = OutboundWebhookSignature.Compute(
            "test-secret",
            DateTimeOffset.FromUnixTimeSeconds(1_700_000_789),
            "{\"data\":\"value\"}");

        emptyPayloadSig.Should().MatchRegex("^[a-f0-9]{64}$");
        emptyPayloadSig.Should().NotBe(normalSig);
    }

    [Fact]
    public void Compute_ShouldThrow_WhenSigningSecretIsNull()
    {
        // This tests delegated BCL behavior — production code has no explicit guard.
        var act = () => OutboundWebhookSignature.Compute(
            null!,
            DateTimeOffset.FromUnixTimeSeconds(1_700_000_789),
            "{}");

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Compute_ShouldHandleLargePayload_WithoutError()
    {
        var payload = new string('a', 100_000);

        var signature = OutboundWebhookSignature.Compute(
            "test-secret",
            DateTimeOffset.FromUnixTimeSeconds(1_700_000_999),
            payload);

        signature.Should().MatchRegex("^[a-f0-9]{64}$");
    }
}
