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

    // Use a per-test Random instance for deterministic reproducibility.
    // Static Random is not thread-safe; per-method instantiation avoids races.
    private static Random CreateRng() => new(42);

    private static string RandomString(Random rng, int minLen, int maxLen)
    {
        var length = rng.Next(minLen, maxLen + 1);
        var chars = new char[length];
        for (int i = 0; i < length; i++)
        {
            // Mix of ASCII, unicode, control chars, and null bytes
            var category = rng.Next(11);
            chars[i] = category switch
            {
                0 => '\0',                                 // null byte
                1 => (char)rng.Next(1, 32),                // control chars (skip null)
                2 => (char)rng.Next(0x4E00, 0x4F00),       // CJK
                3 => (char)rng.Next(0x0600, 0x0700),       // Arabic
                4 => (char)rng.Next(0x0300, 0x0370),       // combining diacriticals
                5 => (char)rng.Next(0x2000, 0x2070),       // general punctuation / special
                _ => (char)rng.Next(0x20, 0x7F),           // printable ASCII
            };
        }
        return new string(chars);
    }

    // ─────────────────────── Board creation sweep ───────────────────────

    [Fact]
    public async Task BoardCreation_100RandomPayloads_Never500()
    {
        await EnsureAuthenticatedAsync();
        var rng = CreateRng();

        for (int i = 0; i < 100; i++)
        {
            var name = RandomString(rng, 0, 200);
            var description = rng.Next(2) == 0 ? null : RandomString(rng, 0, 2000);

            var response = await _client.PostAsJsonAsync("/api/boards",
                new CreateBoardDto(name, description));

            ((int)response.StatusCode).Should().BeLessThan(500,
                $"Board creation returned 500 on iteration {i} for name [{name.Length} chars]");
        }
    }

    // ─────────────────────── Capture creation sweep ───────────────────────

    [Fact]
    public async Task CaptureCreation_100RandomPayloads_Never500()
    {
        await EnsureAuthenticatedAsync();
        var rng = CreateRng();

        for (int i = 0; i < 100; i++)
        {
            var text = RandomString(rng, 0, 25_000);
            var boardId = rng.Next(3) == 0 ? (Guid?)Guid.NewGuid() : null;

            var response = await _client.PostAsJsonAsync("/api/capture/items",
                new CreateCaptureItemDto(boardId, text));

            ((int)response.StatusCode).Should().BeLessThan(500,
                $"Capture creation returned 500 on iteration {i} for text [{text.Length} chars]");
        }
    }

    // ─────────────────────── Card creation sweep ───────────────────────

    [Fact]
    public async Task CardCreation_RandomPayloads_Never500()
    {
        await EnsureAuthenticatedAsync();
        var rng = CreateRng();

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
            var title = RandomString(rng, 0, 300);
            var description = rng.Next(2) == 0 ? null : RandomString(rng, 0, 3000);

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
        var rng = CreateRng();

        for (int i = 0; i < 50; i++)
        {
            var query = RandomString(rng, 0, 500);

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

        var rng = CreateRng();

        for (int i = 0; i < 50; i++)
        {
            var name = RandomString(rng, 0, 100);
            var wipLimit = rng.Next(4) switch
            {
                0 => (int?)null,
                1 => 0,
                2 => -1,
                _ => rng.Next(1, 1000)
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

        var rng = CreateRng();

        for (int i = 0; i < 50; i++)
        {
            var name = RandomString(rng, 0, 50);
            var color = rng.Next(3) switch
            {
                0 => RandomString(rng, 0, 10),     // random string, not a valid hex
                1 => $"#{rng.Next(0xFFFFFF):X6}",   // valid hex
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
    public async Task AuthenticatedEndpoints_NeverReturn500(string method, string path)
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

        // Fail the test when any 500 is returned -- this is the core "no 500s" assertion.
        ((int)response.StatusCode).Should().BeLessThan(500,
            $"{method} {path} returned {(int)response.StatusCode}");
    }
}
