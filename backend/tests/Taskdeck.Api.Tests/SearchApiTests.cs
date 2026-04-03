using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Xunit;

namespace Taskdeck.Api.Tests;

public class SearchApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public SearchApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Search_ShouldReturnUnauthorized_WhenNoToken()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/search?q=test");

        await ApiTestHarness.AssertUnauthorizedAsync(response);
    }

    [Fact]
    public async Task Search_EmptyQuery_ShouldReturnOk()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "search-empty");

        var response = await client.GetAsync("/api/search?q=");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public async Task Search_WithQuery_ShouldReturnResults()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "search-query");
        // Create a board to have something to search
        await ApiTestHarness.CreateBoardAsync(client, "searchable-target");

        var response = await client.GetAsync("/api/search?q=searchable");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("boards", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("cards", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Search_WithPagination_ShouldRespectMaxResultsAndOffset()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "search-page");

        var response = await client.GetAsync("/api/search?q=test&maxResults=5&offset=0");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("maxResults", out var maxProp).Should().BeTrue();
        maxProp.GetInt32().Should().Be(5);
        doc.RootElement.TryGetProperty("offset", out var offsetProp).Should().BeTrue();
        offsetProp.GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Search_WithSpecialCharacters_ShouldNotBreak()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "search-special");

        var response = await client.GetAsync(
            $"/api/search?q={Uri.EscapeDataString("test <script>alert('xss')</script>")}");

        // Should not 500 — either 200 or a validation error
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task Search_CrossUserIsolation_ShouldNotReturnOtherUsersBoards()
    {
        // User A creates a board with a unique keyword
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "search-iso-a");
        var uniqueKeyword = $"isolated-{Guid.NewGuid():N}";
        await ApiTestHarness.CreateBoardAsync(clientA, uniqueKeyword);

        // User B searches for that keyword
        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "search-iso-b");

        var response = await clientB.GetAsync($"/api/search?q={uniqueKeyword}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<GlobalSearchResultDto>();
        result.Should().NotBeNull();
        result!.Boards.Should().BeEmpty(
            "user B should not see user A's board in search results");
    }
}
