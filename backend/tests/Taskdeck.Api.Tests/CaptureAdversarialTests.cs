using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Extended adversarial tests for capture/inbox endpoint.
/// Exercises binary data, null bytes, very long strings, nested JSON,
/// and random binary content — verifying NO 500 responses.
/// </summary>
public class CaptureAdversarialTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private bool _isAuthenticated;

    public CaptureAdversarialTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task EnsureAuthenticatedAsync()
    {
        if (_isAuthenticated) return;
        await ApiTestHarness.AuthenticateAsync(_client, "capture-adversarial");
        _isAuthenticated = true;
    }

    // ─────────────────────── Very long strings ───────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1000)]
    [InlineData(20_000)]     // at limit
    [InlineData(20_001)]     // just over limit
    [InlineData(100_000)]    // far over limit
    public async Task CaptureItem_WithVariousTextLengths_NeverReturns500(int length)
    {
        await EnsureAuthenticatedAsync();

        var text = length == 0 ? "" : new string('c', length);
        var response = await _client.PostAsJsonAsync("/api/capture/items",
            new CreateCaptureItemDto(null, text));

        ((int)response.StatusCode).Should().BeLessThan(500,
            $"Capture returned 500 for text of {length} chars");
    }

    // ─────────────────────── Null bytes and control characters ───────────────────────

    [Theory]
    [InlineData("\u0000")]
    [InlineData("text\u0000with\u0000null\u0000bytes")]
    [InlineData("\u0001\u0002\u0003\u0004\u0005\u0006\u0007")]
    [InlineData("\u0008\u000B\u000C\u000E\u000F\u0010")]
    [InlineData("\x1B[31mcolored\x1B[0m")]
    [InlineData("\r\n\r\n\r\n")]
    [InlineData("\t\t\t\t\t")]
    [InlineData("before\u0000after")]
    public async Task CaptureItem_WithControlChars_NeverReturns500(string text)
    {
        await EnsureAuthenticatedAsync();

        var response = await _client.PostAsJsonAsync("/api/capture/items",
            new CreateCaptureItemDto(null, text));

        ((int)response.StatusCode).Should().BeLessThan(500,
            $"Capture returned 500 for text with control chars");
    }

    // ─────────────────────── Unicode edge cases ───────────────────────

    [Theory]
    [InlineData("\uFEFF")]                        // BOM
    [InlineData("\uFFFD")]                        // replacement character
    [InlineData("\u200B")]                        // zero-width space
    [InlineData("\u200E")]                        // LTR mark
    [InlineData("\u202E")]                        // RTL override
    [InlineData("\u0301")]                        // combining accent
    [InlineData("e\u0301")]                       // decomposed e-acute
    [InlineData("\u00E9")]                        // precomposed e-acute
    [InlineData("👨‍👩‍👧‍👦")]                   // family emoji
    [InlineData("𝕋𝕖𝕤𝕥")]                        // math bold
    [InlineData("田中太郎")]                       // CJK
    [InlineData("مرحبا")]                         // Arabic RTL
    [InlineData("\u0E01\u0E38")]                  // Thai combining
    [InlineData("\uDBFF\uDFFF")]                  // max surrogate pair
    public async Task CaptureItem_WithUnicodeEdgeCases_NeverReturns500(string text)
    {
        await EnsureAuthenticatedAsync();

        var response = await _client.PostAsJsonAsync("/api/capture/items",
            new CreateCaptureItemDto(null, text));

        ((int)response.StatusCode).Should().BeLessThan(500,
            $"Capture returned 500 for unicode edge case");
    }

    // ─────────────────────── Nested JSON as text content ───────────────────────

    [Theory]
    [InlineData("{\"nested\": true}")]
    [InlineData("[1, 2, 3]")]
    [InlineData("{\"__proto__\": {\"admin\": true}}")]
    [InlineData("{\"constructor\": {\"prototype\": {\"isAdmin\": true}}}")]
    [InlineData("{\"action\": \"delete\", \"target\": \"all_boards\"}")]
    [InlineData("{{7*7}}")]
    [InlineData("${7*7}")]
    [InlineData("#{7*7}")]
    public async Task CaptureItem_WithNestedJsonAndTemplates_NeverReturns500(string text)
    {
        await EnsureAuthenticatedAsync();

        var response = await _client.PostAsJsonAsync("/api/capture/items",
            new CreateCaptureItemDto(null, text));

        ((int)response.StatusCode).Should().BeLessThan(500,
            $"Capture returned 500 for nested JSON/template content");

        if (response.IsSuccessStatusCode)
        {
            var item = await response.Content.ReadFromJsonAsync<CaptureItemDto>();
            item.Should().NotBeNull();
            item!.RawText.Should().Contain(text,
                "nested JSON should be stored as literal text, not interpreted");
        }
    }

    // ─────────────────────── XSS/injection payloads ───────────────────────

    [Theory]
    [InlineData("<script>alert(document.cookie)</script>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("<svg onload=alert(1)>")]
    [InlineData("'; DROP TABLE capture_items; --")]
    [InlineData("\" OR 1=1 --")]
    [InlineData("Robert'); DROP TABLE students;--")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    public async Task CaptureItem_WithInjectionPayloads_StoredVerbatim(string text)
    {
        await EnsureAuthenticatedAsync();

        var response = await _client.PostAsJsonAsync("/api/capture/items",
            new CreateCaptureItemDto(null, text));

        ((int)response.StatusCode).Should().BeLessThan(500,
            $"Capture returned 500 for injection payload");

        if (response.IsSuccessStatusCode)
        {
            var item = await response.Content.ReadFromJsonAsync<CaptureItemDto>();
            item.Should().NotBeNull();
            // Injection payloads should be stored verbatim, not sanitized
            item!.RawText.Should().Contain(text);
        }
    }

    // ─────────────────────── Optional field adversarial ───────────────────────

    [Theory]
    [InlineData("<script>", null, null)]
    [InlineData("test", "<script>alert(1)</script>", null)]
    [InlineData("test", null, "<script>")]
    [InlineData("test", "'; DROP TABLE capture_items; --", "'; DROP TABLE capture_items; --")]
    public async Task CaptureItem_WithAdversarialOptionalFields_NeverReturns500(
        string text, string? titleHint, string? externalRef)
    {
        await EnsureAuthenticatedAsync();

        var response = await _client.PostAsJsonAsync("/api/capture/items",
            new CreateCaptureItemDto(null, text, TitleHint: titleHint, ExternalRef: externalRef));

        ((int)response.StatusCode).Should().BeLessThan(500,
            "Capture returned 500 for adversarial optional fields");
    }

    // ─────────────────────── Malformed JSON bodies ───────────────────────

    [Theory]
    [InlineData("{")]
    [InlineData("[")]
    [InlineData("null")]
    [InlineData("\"just a string\"")]
    [InlineData("12345")]
    [InlineData("{\"text\": null}")]
    [InlineData("{\"boardId\": \"not-a-guid\", \"text\": \"test\"}")]
    [InlineData("{\"text\": 12345}")]
    public async Task CaptureItem_WithMalformedJson_NeverReturns500(string body)
    {
        await EnsureAuthenticatedAsync();

        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/capture/items", content);

        ((int)response.StatusCode).Should().BeLessThan(500,
            $"Capture returned 500 for malformed JSON: {body}");
    }

    // ─────────────────────── Board ID adversarial ───────────────────────

    [Fact]
    public async Task CaptureItem_WithEmptyGuidBoardId_NeverReturns500()
    {
        await EnsureAuthenticatedAsync();

        var response = await _client.PostAsJsonAsync("/api/capture/items",
            new CreateCaptureItemDto(Guid.Empty, "test text"));

        ((int)response.StatusCode).Should().BeLessThan(500,
            "Capture returned 500 for Guid.Empty board ID");
    }

    [Fact]
    public async Task CaptureItem_WithNonexistentBoardId_NeverReturns500()
    {
        await EnsureAuthenticatedAsync();

        var response = await _client.PostAsJsonAsync("/api/capture/items",
            new CreateCaptureItemDto(Guid.NewGuid(), "test text"));

        ((int)response.StatusCode).Should().BeLessThan(500,
            "Capture returned 500 for nonexistent board ID");
    }
}
