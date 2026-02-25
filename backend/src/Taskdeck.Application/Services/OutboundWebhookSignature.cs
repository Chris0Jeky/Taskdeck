using System.Security.Cryptography;
using System.Text;

namespace Taskdeck.Application.Services;

public static class OutboundWebhookSignature
{
    public static string Compute(string signingSecret, DateTimeOffset timestamp, string payload)
    {
        var canonical = $"{timestamp.ToUnixTimeSeconds()}.{payload}";
        var secretBytes = Encoding.UTF8.GetBytes(signingSecret);
        var payloadBytes = Encoding.UTF8.GetBytes(canonical);

        using var hmac = new HMACSHA256(secretBytes);
        var hash = hmac.ComputeHash(payloadBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
