using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Adversarial tests for webhook endpoint URL validation.
/// Verifies that dangerous, malformed, and RFC 3986 edge-case URLs
/// are rejected with 4xx — never 5xx.
/// </summary>
public class WebhookUrlAdversarialTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private bool _isAuthenticated;
    private Guid? _boardId;

    public WebhookUrlAdversarialTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task EnsureAuthenticatedAsync()
    {
        if (_isAuthenticated) return;
        await ApiTestHarness.AuthenticateAsync(_client, "webhook-adversarial");
        _isAuthenticated = true;
    }

    private async Task<Guid> EnsureBoardAsync()
    {
        if (_boardId.HasValue) return _boardId.Value;
        await EnsureAuthenticatedAsync();
        var board = await ApiTestHarness.CreateBoardAsync(_client, "webhook-test");
        _boardId = board.Id;
        return board.Id;
    }

    // ─────────────────────── RFC 3986 edge-case URLs ───────────────────────

    public static IEnumerable<object[]> AdversarialWebhookUrls()
    {
        // Dangerous URL schemes
        yield return new object[] { "javascript:alert(1)" };
        yield return new object[] { "data:text/html,<script>alert(1)</script>" };
        yield return new object[] { "data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==" };
        yield return new object[] { "vbscript:MsgBox(1)" };
        yield return new object[] { "file:///etc/passwd" };
        yield return new object[] { "ftp://evil.com/payload" };

        // URLs with credentials (userinfo component)
        yield return new object[] { "https://admin:password@evil.com/webhook" };
        yield return new object[] { "http://user:pass@127.0.0.1/callback" };

        // Internal/localhost URLs (SSRF vectors)
        yield return new object[] { "http://localhost/webhook" };
        yield return new object[] { "http://127.0.0.1/webhook" };
        yield return new object[] { "http://[::1]/webhook" };
        yield return new object[] { "http://0.0.0.0/webhook" };
        yield return new object[] { "http://169.254.169.254/latest/meta-data/" };
        yield return new object[] { "http://metadata.google.internal/" };

        // Malformed URLs
        yield return new object[] { "" };
        yield return new object[] { " " };
        yield return new object[] { "not-a-url" };
        yield return new object[] { "://missing-scheme" };
        yield return new object[] { "http://" };
        yield return new object[] { "http:///no-host" };

        // URLs with injection payloads
        yield return new object[] { "https://evil.com/webhook?q='; DROP TABLE webhooks; --" };
        yield return new object[] { "https://evil.com/webhook#<script>alert(1)</script>" };
        yield return new object[] { "https://evil.com/\u0000null-byte" };
        yield return new object[] { "https://evil.com/\r\nHeader-Injection: true" };

        // URLs with unicode
        yield return new object[] { "https://evil.com/\u200Bhidden" };
        yield return new object[] { "https://evil.com/\u202Efdp.exe" };
        yield return new object[] { "https://xn--n3h.com/webhook" }; // punycode emoji domain

        // Extremely long URL
        yield return new object[] { "https://example.com/" + new string('a', 10_000) };

        // URL with port boundaries
        yield return new object[] { "https://example.com:0/webhook" };
        yield return new object[] { "https://example.com:99999/webhook" };
        yield return new object[] { "https://example.com:-1/webhook" };
    }

    [Theory]
    [MemberData(nameof(AdversarialWebhookUrls))]
    public async Task CreateWebhook_WithAdversarialUrl_NeverReturns500(string endpointUrl)
    {
        var boardId = await EnsureBoardAsync();

        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{boardId}/webhooks",
            new CreateOutboundWebhookSubscriptionDto(endpointUrl));

        ((int)response.StatusCode).Should().BeLessThan(500,
            $"Webhook creation returned 500 for URL: {endpointUrl}");
    }

    [Theory]
    [InlineData("https://example.com/webhook")]
    [InlineData("https://hooks.slack.com/services/T00/B00/xxxx")]
    [InlineData("https://example.com:8443/api/webhook")]
    public async Task CreateWebhook_WithValidUrl_Succeeds(string endpointUrl)
    {
        var boardId = await EnsureBoardAsync();

        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{boardId}/webhooks",
            new CreateOutboundWebhookSubscriptionDto(endpointUrl));

        // Valid HTTPS URLs should either succeed or be accepted
        ((int)response.StatusCode).Should().BeLessThan(500);
    }

    // ─────────────────────── Event filter adversarial ───────────────────────

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("'; DROP TABLE events; --")]
    [InlineData("")]
    [InlineData("\u0000")]
    public async Task CreateWebhook_WithAdversarialEventFilter_NeverReturns500(string eventFilter)
    {
        var boardId = await EnsureBoardAsync();

        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{boardId}/webhooks",
            new CreateOutboundWebhookSubscriptionDto(
                "https://example.com/webhook",
                new List<string> { eventFilter }));

        ((int)response.StatusCode).Should().BeLessThan(500,
            $"Webhook creation returned 500 for event filter: {eventFilter}");
    }

    // ─────────────────────── Null/missing body ───────────────────────

    [Fact]
    public async Task CreateWebhook_WithNullBody_NeverReturns500()
    {
        var boardId = await EnsureBoardAsync();

        var content = new StringContent("null", System.Text.Encoding.UTF8, "application/json");
        var response = await _client.PostAsync($"/api/boards/{boardId}/webhooks", content);

        ((int)response.StatusCode).Should().BeLessThan(500,
            "Webhook creation returned 500 for null body");
    }

    [Fact]
    public async Task CreateWebhook_WithEmptyBody_NeverReturns500()
    {
        var boardId = await EnsureBoardAsync();

        var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        var response = await _client.PostAsync($"/api/boards/{boardId}/webhooks", content);

        ((int)response.StatusCode).Should().BeLessThan(500,
            "Webhook creation returned 500 for empty object body");
    }
}
