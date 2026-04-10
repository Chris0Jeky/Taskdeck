using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Meta-test: for any well-formed request with random content, the API never returns
/// 500 Internal Server Error with a stack trace. This is a comprehensive sweep across
/// multiple endpoints with randomized but structurally valid payloads.
/// </summary>
public class No500sMetaTest : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private bool _isAuthenticated;

    public No500sMetaTest(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task EnsureAuthenticatedAsync()
    {
        if (_isAuthenticated) return;
        await ApiTestHarness.AuthenticateAsync(_client, "no500s-meta");
        _isAuthenticated = true;
    }

    // ─────────────────────── Random content generators ───────────────────────

    private static readonly Random Rng = new(42); // deterministic seed for reproducibility

    private static string RandomString(int minLen, int maxLen)
    {
        var length = Rng.Next(minLen, maxLen + 1);
        var chars = new char[length];
        for (int i = 0; i < length; i++)
        {
            // Mix of ASCII, unicode, and control chars
            var category = Rng.Next(10);
            chars[i] = category switch
            {
                0 => (char)Rng.Next(1, 32),               // control chars (skip null)
                1 => (char)Rng.Next(0x4E00, 0x4F00),      // CJK
                2 => (char)Rng.Next(0x0600, 0x0700),      // Arabic
                3 => (char)Rng.Next(0x0300, 0x0370),      // combining diacriticals
                4 => (char)Rng.Next(0x2000, 0x2070),      // general punctuation / special
                _ => (char)Rng.Next(0x20, 0x7F),          // printable ASCII
            };
        }
        return new string(chars);
    }

    // ─────────────────────── Board creation sweep ───────────────────────

    [Fact]
    public async Task BoardCreation_100RandomPayloads_Never500()
    {
        await EnsureAuthenticatedAsync();

        for (int i = 0; i < 100; i++)
        {
            var name = RandomString(0, 200);
            var description = Rng.Next(2) == 0 ? null : RandomString(0, 2000);

            var response = await _client.PostAsJsonAsync("/api/boards",
                new CreateBoardDto(name, description));

            ((int)response.StatusCode).Should().BeLessThan(500,
                $"Board creation returned 500 on iteration {i} for name [{name.Length} chars]");

            // Verify no stack trace in response body
            if ((int)response.StatusCode >= 500)
            {
                var body = await response.Content.ReadAsStringAsync();
                body.Should().NotContain("System.", "500 response should not contain stack traces");
                body.Should().NotContain("at Taskdeck.", "500 response should not contain stack traces");
            }
        }
    }

    // ─────────────────────── Capture creation sweep ───────────────────────

    [Fact]
    public async Task CaptureCreation_100RandomPayloads_Never500()
    {
        await EnsureAuthenticatedAsync();

        for (int i = 0; i < 100; i++)
        {
            var text = RandomString(0, 25_000);
            var boardId = Rng.Next(3) == 0 ? (Guid?)Guid.NewGuid() : null;

            var response = await _client.PostAsJsonAsync("/api/capture/items",
                new CreateCaptureItemDto(boardId, text));

            ((int)response.StatusCode).Should().BeLessThan(500,
                $"Capture creation returned 500 on iteration {i} for text [{text.Length} chars]");

            if ((int)response.StatusCode >= 500)
            {
                var body = await response.Content.ReadAsStringAsync();
                body.Should().NotContain("System.", "500 response should not contain stack traces");
            }
        }
    }

    // ─────────────────────── Card creation sweep ───────────────────────

    [Fact]
    public async Task CardCreation_RandomPayloads_Never500()
    {
        await EnsureAuthenticatedAsync();

        // Create a board and column for card tests
        var boardResponse = await _client.PostAsJsonAsync("/api/boards",
            new CreateBoardDto($"no500s-cards-{Guid.NewGuid():N}", null));
        boardResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var board = await boardResponse.Content.ReadFromJsonAsync<BoardDto>();

        var colResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board!.Id}/columns",
            new CreateColumnDto(board.Id, "TestCol", null, null));
        colResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var col = await colResponse.Content.ReadFromJsonAsync<ColumnDto>();

        for (int i = 0; i < 50; i++)
        {
            var title = RandomString(0, 300);
            var description = Rng.Next(2) == 0 ? null : RandomString(0, 3000);

            var response = await _client.PostAsJsonAsync(
                $"/api/boards/{board.Id}/cards",
                new CreateCardDto(board.Id, col!.Id, title, description, null, null));

            ((int)response.StatusCode).Should().BeLessThan(500,
                $"Card creation returned 500 on iteration {i} for title [{title.Length} chars]");
        }
    }

    // ─────────────────────── Search sweep ───────────────────────

    [Fact]
    public async Task Search_50RandomQueries_Never500()
    {
        await EnsureAuthenticatedAsync();

        for (int i = 0; i < 50; i++)
        {
            var query = RandomString(0, 500);

            var response = await _client.GetAsync(
                $"/api/search?q={Uri.EscapeDataString(query)}");

            ((int)response.StatusCode).Should().BeLessThan(500,
                $"Search returned 500 on iteration {i} for query [{query.Length} chars]");
        }
    }

    // ─────────────────────── Column creation sweep ───────────────────────

    [Fact]
    public async Task ColumnCreation_RandomPayloads_Never500()
    {
        await EnsureAuthenticatedAsync();

        var boardResponse = await _client.PostAsJsonAsync("/api/boards",
            new CreateBoardDto($"no500s-cols-{Guid.NewGuid():N}", null));
        boardResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var board = await boardResponse.Content.ReadFromJsonAsync<BoardDto>();

        for (int i = 0; i < 50; i++)
        {
            var name = RandomString(0, 100);
            var wipLimit = Rng.Next(4) switch
            {
                0 => (int?)null,
                1 => 0,
                2 => -1,
                _ => Rng.Next(1, 1000)
            };

            var response = await _client.PostAsJsonAsync(
                $"/api/boards/{board!.Id}/columns",
                new CreateColumnDto(board.Id, name, null, wipLimit));

            ((int)response.StatusCode).Should().BeLessThan(500,
                $"Column creation returned 500 on iteration {i} for name [{name.Length} chars]");
        }
    }

    // ─────────────────────── Label creation sweep ───────────────────────

    [Fact]
    public async Task LabelCreation_RandomPayloads_Never500()
    {
        await EnsureAuthenticatedAsync();

        var boardResponse = await _client.PostAsJsonAsync("/api/boards",
            new CreateBoardDto($"no500s-labels-{Guid.NewGuid():N}", null));
        boardResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var board = await boardResponse.Content.ReadFromJsonAsync<BoardDto>();

        for (int i = 0; i < 50; i++)
        {
            var name = RandomString(0, 50);
            var color = Rng.Next(3) switch
            {
                0 => RandomString(0, 10),       // random string, not a valid hex
                1 => $"#{Rng.Next(0xFFFFFF):X6}", // valid hex
                _ => ""
            };

            var response = await _client.PostAsJsonAsync(
                $"/api/boards/{board!.Id}/labels",
                new { name, colorHex = color });

            ((int)response.StatusCode).Should().BeLessThan(500,
                $"Label creation returned 500 on iteration {i}");
        }
    }

    // ─────────────────────── Cross-endpoint: 500 response body check ───────────────────────

    [Theory]
    [InlineData("GET", "/api/boards")]
    [InlineData("GET", "/api/search?q=test")]
    [InlineData("GET", "/api/capture/items")]
    [InlineData("GET", "/api/automation/proposals")]
    [InlineData("GET", "/api/notifications")]
    public async Task AuthenticatedEndpoints_NeverReturn500WithStackTrace(string method, string path)
    {
        await EnsureAuthenticatedAsync();

        HttpResponseMessage response;
        if (method == "GET")
        {
            response = await _client.GetAsync(path);
        }
        else
        {
            response = await _client.PostAsync(path,
                new StringContent("{}", Encoding.UTF8, "application/json"));
        }

        if ((int)response.StatusCode >= 500)
        {
            var body = await response.Content.ReadAsStringAsync();
            body.Should().NotContain("StackTrace",
                $"{method} {path} returned 500 with stack trace exposed");
            body.Should().NotContain("at Taskdeck.",
                $"{method} {path} returned 500 with internal exception details exposed");
        }
    }
}
