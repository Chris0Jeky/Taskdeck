using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Adversarial input tests for API endpoints.
/// Key property: NO 500 Internal Server Error from any random/malicious input.
/// All bad input should result in 400/422 (validation) or be accepted and stored verbatim.
/// </summary>
public class AdversarialInputApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private bool _isAuthenticated;

    public AdversarialInputApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task EnsureAuthenticatedAsync()
    {
        if (_isAuthenticated) return;
        await ApiTestHarness.AuthenticateAsync(_client, "adversarial-tester");
        _isAuthenticated = true;
    }

    // ─────────────────────── Adversarial payloads ───────────────────────

    public static IEnumerable<object[]> AdversarialBoardNames()
    {
        yield return new object[] { "<script>alert('xss')</script>" };
        yield return new object[] { "'; DROP TABLE boards; --" };
        yield return new object[] { "\" OR 1=1 --" };
        yield return new object[] { "<img src=x onerror=alert(1)>" };
        yield return new object[] { "{{constructor.constructor('return this')()}}" };
        yield return new object[] { "Board\u0000Name" };           // null byte
        yield return new object[] { "\uFEFFBoard" };               // BOM prefix
        yield return new object[] { "Board\u200BName" };           // zero-width space
        yield return new object[] { "Board\u202EemaN" };           // RTL override
        yield return new object[] { "田中太郎のボード" };            // CJK
        yield return new object[] { "مرحبا" };                     // Arabic
        yield return new object[] { "👨‍👩‍👧‍👦" };               // Multi-codepoint emoji
        yield return new object[] { "e\u0301" };                   // Combining character
        yield return new object[] { "\x01\x02\x03" };              // Control chars
        yield return new object[] { "\x1B[31mRed\x1B[0m" };       // ANSI escape
        yield return new object[] { "javascript:alert(1)" };
        yield return new object[] { "data:text/html,<h1>hi</h1>" };
        yield return new object[] { "{\"nested\": true}" };        // JSON in string
        yield return new object[] { new string('a', 101) };        // Over 100 char limit
    }

    // ─────────────────────── Board creation with adversarial names ───────────────────────

    [Theory]
    [MemberData(nameof(AdversarialBoardNames))]
    public async Task CreateBoard_WithAdversarialName_NeverReturns500(string name)
    {
        await EnsureAuthenticatedAsync();

        var response = await _client.PostAsJsonAsync("/api/boards", new CreateBoardDto(name, null));

        // Must be 2xx (accepted) or 4xx (validation error) — never 5xx
        ((int)response.StatusCode).Should().BeLessThan(500,
            $"API returned 500 for board name [{name.Length} chars]: {await response.Content.ReadAsStringAsync()}");

        if (response.IsSuccessStatusCode)
        {
            var board = await response.Content.ReadFromJsonAsync<BoardDto>();
            board.Should().NotBeNull();
            // Adversarial strings should be stored verbatim if accepted
            board!.Name.Should().NotBeNullOrEmpty();
        }
    }

    // ─────────────────────── Large payload tests ───────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(101)]
    [InlineData(1000)]
    [InlineData(10_000)]
    [InlineData(100_000)]
    public async Task CreateBoard_WithVariousNameLengths_NeverReturns500(int length)
    {
        await EnsureAuthenticatedAsync();

        var name = length == 0 ? "" : new string('b', length);
        var response = await _client.PostAsJsonAsync("/api/boards", new CreateBoardDto(name, null));

        ((int)response.StatusCode).Should().BeLessThan(500,
            $"API returned 500 for board name of {length} chars");

        if (length >= 1 && length <= 100)
        {
            response.StatusCode.Should().Be(HttpStatusCode.Created);
        }
        else
        {
            // 0 or >100: should be validation error
            response.StatusCode.Should().BeOneOf(
                HttpStatusCode.BadRequest,
                HttpStatusCode.UnprocessableEntity);
        }
    }

    // ─────────────────────── Malformed JSON body tests ───────────────────────

    [Theory]
    [InlineData("{")]
    [InlineData("[")]
    [InlineData("{{")]
    [InlineData("{\"name\":")]
    [InlineData("not json at all")]
    [InlineData("<xml>board</xml>")]
    [InlineData("")]
    public async Task CreateBoard_WithMalformedJson_NeverReturns500(string body)
    {
        await EnsureAuthenticatedAsync();

        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/boards", content);

        ((int)response.StatusCode).Should().BeLessThan(500,
            $"API returned 500 for malformed JSON body: {body}");
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("text/html")]
    [InlineData("application/xml")]
    [InlineData("multipart/form-data")]
    public async Task CreateBoard_WithWrongContentType_NeverReturns500(string contentType)
    {
        await EnsureAuthenticatedAsync();

        var content = new StringContent("{\"name\": \"test\"}", Encoding.UTF8, contentType);
        var response = await _client.PostAsync("/api/boards", content);

        ((int)response.StatusCode).Should().BeLessThan(500,
            $"API returned 500 for content type: {contentType}");
    }

    // ─────────────────────── Card creation with adversarial inputs ───────────────────────

    [Theory]
    [InlineData("<script>alert('xss')</script>")]
    [InlineData("'; DROP TABLE cards; --")]
    [InlineData("Card\u0000Title")]
    [InlineData("\uFEFFCard")]
    [InlineData("👨‍👩‍👧‍👦")]
    [InlineData("{\"nested\": \"json\"}")]
    public async Task CreateCard_WithAdversarialTitle_NeverReturns500(string title)
    {
        await EnsureAuthenticatedAsync();

        // Create a board first
        var boardResponse = await _client.PostAsJsonAsync("/api/boards",
            new CreateBoardDto($"adv-card-{Guid.NewGuid():N}", null));
        boardResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var board = await boardResponse.Content.ReadFromJsonAsync<BoardDto>();

        // Create a column
        var colResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board!.Id}/columns",
            new CreateColumnDto(board.Id, "TestCol", null, null));
        colResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var col = await colResponse.Content.ReadFromJsonAsync<ColumnDto>();

        // Create card with adversarial title
        var cardResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/cards",
            new CreateCardDto(board.Id, col!.Id, title, null, null, null));

        ((int)cardResponse.StatusCode).Should().BeLessThan(500,
            $"API returned 500 for card title: {title}");

        if (cardResponse.IsSuccessStatusCode)
        {
            var card = await cardResponse.Content.ReadFromJsonAsync<CardDto>();
            card.Should().NotBeNull();
            card!.Title.Should().Be(title, "adversarial title should be stored verbatim");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(10_000)]
    public async Task CreateCard_WithVariousTitleLengths_NeverReturns500(int length)
    {
        await EnsureAuthenticatedAsync();

        var boardResponse = await _client.PostAsJsonAsync("/api/boards",
            new CreateBoardDto($"adv-len-{Guid.NewGuid():N}", null));
        boardResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var board = await boardResponse.Content.ReadFromJsonAsync<BoardDto>();

        var colResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board!.Id}/columns",
            new CreateColumnDto(board.Id, "TestCol", null, null));
        colResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var col = await colResponse.Content.ReadFromJsonAsync<ColumnDto>();

        var title = length == 0 ? "" : new string('t', length);
        var cardResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/cards",
            new CreateCardDto(board.Id, col!.Id, title, null, null, null));

        ((int)cardResponse.StatusCode).Should().BeLessThan(500,
            $"API returned 500 for card title of {length} chars");
    }

    // ─────────────────────── Column name adversarial ───────────────────────

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("'; DROP TABLE columns; --")]
    [InlineData("\u0000")]
    [InlineData("\uFEFF")]
    [InlineData("c")]
    [InlineData("Col\u200BName")]
    public async Task CreateColumn_WithAdversarialName_NeverReturns500(string name)
    {
        await EnsureAuthenticatedAsync();

        var boardResponse = await _client.PostAsJsonAsync("/api/boards",
            new CreateBoardDto($"adv-col-{Guid.NewGuid():N}", null));
        boardResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var board = await boardResponse.Content.ReadFromJsonAsync<BoardDto>();

        var response = await _client.PostAsJsonAsync(
            $"/api/boards/{board!.Id}/columns",
            new CreateColumnDto(board.Id, name, null, null));

        ((int)response.StatusCode).Should().BeLessThan(500,
            $"API returned 500 for column name: {name}");
    }

    // ─────────────────────── Auth endpoint adversarial ───────────────────────

    [Theory]
    [InlineData("", "e@e.com", "pass123")]
    [InlineData("user", "", "pass123")]
    [InlineData("user", "e@e.com", "")]
    [InlineData("<script>", "xss@test.com", "password")]
    [InlineData("'; DROP TABLE users; --", "sql@inj.com", "password")]
    [InlineData("user\u0000name", "null@byte.com", "password")]
    [InlineData("a", "e@e.com", "p")]
    public async Task Register_WithAdversarialInputs_NeverReturns500(string username, string email, string password)
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new CreateUserDto(username, email, password));

        ((int)response.StatusCode).Should().BeLessThan(500,
            $"Auth register returned 500 for username={username}, email={email}");
    }

    // ─────────────────────── Search endpoint adversarial ───────────────────────

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("'; DROP TABLE cards; --")]
    [InlineData("\" OR 1=1 --")]
    [InlineData("\u0000")]
    [InlineData("a")]
    [InlineData("")]
    public async Task Search_WithAdversarialQuery_NeverReturns500(string query)
    {
        await EnsureAuthenticatedAsync();

        var response = await _client.GetAsync($"/api/search?q={Uri.EscapeDataString(query)}");

        ((int)response.StatusCode).Should().BeLessThan(500,
            $"Search returned 500 for query: {query}");
    }

    // ─────────────────────── Capture endpoint adversarial ───────────────────────

    [Theory]
    [InlineData("<script>alert('xss')</script>")]
    [InlineData("'; DROP TABLE capture_items; --")]
    [InlineData("Very long content: " + "x")]  // Will be short, but tests the pattern
    [InlineData("\u0000\u0001\u0002")]
    [InlineData("👨‍👩‍👧‍👦 emoji capture")]
    [InlineData("{\"nested\": {\"json\": true}}")]
    public async Task CaptureInbox_WithAdversarialContent_NeverReturns500(string content)
    {
        await EnsureAuthenticatedAsync();

        var response = await _client.PostAsJsonAsync("/api/capture/items",
            new CreateCaptureItemDto(null, content));

        ((int)response.StatusCode).Should().BeLessThan(500,
            $"Capture inbox returned 500 for content: {content}");
    }

    // ─────────────────────── Invalid GUID path parameters ───────────────────────

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("12345")]
    [InlineData("")]
    [InlineData("<script>")]
    [InlineData("'; DROP TABLE boards; --")]
    public async Task GetBoard_WithInvalidGuid_NeverReturns500(string id)
    {
        await EnsureAuthenticatedAsync();

        var response = await _client.GetAsync($"/api/boards/{Uri.EscapeDataString(id)}");

        ((int)response.StatusCode).Should().BeLessThan(500,
            $"GET /api/boards/{id} returned 500");
    }

    // ─────────────────────── Proposal creation with adversarial operations ───────────────────────

    [Fact]
    public async Task CreateBoard_WithXssInDescription_StoredVerbatim()
    {
        await EnsureAuthenticatedAsync();

        var xssPayload = "<script>document.cookie</script><img src=x onerror=alert(1)>";
        var response = await _client.PostAsJsonAsync("/api/boards",
            new CreateBoardDto("XSS-Test-Board", xssPayload));

        // Description within 1000 chars so should succeed
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var board = await response.Content.ReadFromJsonAsync<BoardDto>();
        board.Should().NotBeNull();
        board!.Description.Should().Be(xssPayload,
            "XSS payloads in descriptions should be stored verbatim (escaped at render time)");
    }

    // ─────────────────────── Concurrent adversarial requests ───────────────────────

    [Fact]
    public async Task ConcurrentAdversarialRequests_NeverCause500()
    {
        await EnsureAuthenticatedAsync();

        var adversarialNames = new[]
        {
            "<script>alert(1)</script>",
            "'; DROP TABLE boards; --",
            "Normal Board",
            "👨‍👩‍👧‍👦",
            "田中太郎",
            new string('a', 100),
        };

        var tasks = adversarialNames.Select(name =>
            _client.PostAsJsonAsync("/api/boards", new CreateBoardDto(name, null)));

        var responses = await Task.WhenAll(tasks);

        foreach (var response in responses)
        {
            ((int)response.StatusCode).Should().BeLessThan(500,
                "Concurrent adversarial requests should not cause 500");
        }
    }
}
