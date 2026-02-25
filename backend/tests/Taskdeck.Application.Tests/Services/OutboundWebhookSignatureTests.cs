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
}
