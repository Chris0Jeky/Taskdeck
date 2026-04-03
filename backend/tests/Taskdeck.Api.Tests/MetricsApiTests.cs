using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Xunit;

namespace Taskdeck.Api.Tests;

public class MetricsApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public MetricsApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetBoardMetrics_ShouldReturnUnauthorized_WhenNoToken()
    {
        using var client = _factory.CreateClient();
        var boardId = Guid.NewGuid();

        var response = await client.GetAsync($"/api/metrics/boards/{boardId}");

        await ApiTestHarness.AssertUnauthorizedAsync(response);
    }

    [Fact]
    public async Task GetBoardMetrics_ShouldReturnOk_ForOwnBoard()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "metrics-owner");
        var board = await ApiTestHarness.CreateBoardAsync(client, "metrics-board");

        var response = await client.GetAsync($"/api/metrics/boards/{board.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("boardId", out var boardIdProp).Should().BeTrue();
        boardIdProp.GetString().Should().Be(board.Id.ToString());
    }

    [Fact]
    public async Task GetBoardMetrics_ShouldReturnForbiddenOrNotFound_ForOtherUsersBoard()
    {
        using var ownerClient = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(ownerClient, "metrics-board-owner");
        var board = await ApiTestHarness.CreateBoardAsync(ownerClient, "metrics-private");

        using var outsiderClient = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(outsiderClient, "metrics-outsider");

        var response = await outsiderClient.GetAsync($"/api/metrics/boards/{board.Id}");

        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(response);
    }

    [Fact]
    public async Task GetBoardMetrics_NonExistentBoard_ShouldReturnNotFoundOrForbidden()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "metrics-notfound");

        var response = await client.GetAsync($"/api/metrics/boards/{Guid.NewGuid()}");

        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(response);
    }

    [Fact]
    public async Task GetBoardMetrics_WithCustomDateRange_ShouldReturnOk()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "metrics-daterange");
        var board = await ApiTestHarness.CreateBoardAsync(client, "metrics-range");

        var from = DateTimeOffset.UtcNow.AddDays(-7).ToString("o");
        var to = DateTimeOffset.UtcNow.ToString("o");

        var response = await client.GetAsync(
            $"/api/metrics/boards/{board.Id}?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetBoardMetrics_DefaultRange_ShouldReturn30Days()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "metrics-default");
        var board = await ApiTestHarness.CreateBoardAsync(client, "metrics-default-range");

        var response = await client.GetAsync($"/api/metrics/boards/{board.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);

        // Verify from/to are present and represent roughly 30 days
        doc.RootElement.TryGetProperty("from", out var fromProp).Should().BeTrue();
        doc.RootElement.TryGetProperty("to", out var toProp).Should().BeTrue();
        var fromDate = DateTimeOffset.Parse(fromProp.GetString()!);
        var toDate = DateTimeOffset.Parse(toProp.GetString()!);
        (toDate - fromDate).TotalDays.Should().BeApproximately(30, 1);
    }

    [Fact]
    public async Task GetBoardMetrics_EmptyBoard_ShouldReturnMetricsNotError()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "metrics-empty");
        var board = await ApiTestHarness.CreateBoardAsync(client, "metrics-empty-board");

        var response = await client.GetAsync($"/api/metrics/boards/{board.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "metrics for a board with no completed cards should return 200, not an error");
    }
}
