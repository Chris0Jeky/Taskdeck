using System.Net;
using System.Text;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Xunit;

namespace Taskdeck.Api.Tests;

public class MetricsExportApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public MetricsExportApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ExportCsv_ShouldReturnUnauthorized_WhenNoToken()
    {
        using var client = _factory.CreateClient();
        var boardId = Guid.NewGuid();

        var response = await client.GetAsync($"/api/metrics/boards/{boardId}/export");

        await ApiTestHarness.AssertUnauthorizedAsync(response);
    }

    [Fact]
    public async Task ExportCsv_ShouldReturnCsvFile_ForOwnBoard()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "export-owner");
        var board = await ApiTestHarness.CreateBoardAsync(client, "export-board");

        var response = await client.GetAsync($"/api/metrics/boards/{board.Id}/export");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        response.Content.Headers.ContentDisposition.Should().NotBeNull();
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        response.Content.Headers.ContentDisposition.FileName.Should().Contain("board-metrics-");
        response.Content.Headers.ContentDisposition.FileName.Should().EndWith(".csv");
    }

    [Fact]
    public async Task ExportCsv_ShouldContainSchemaVersion()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "export-schema");
        var board = await ApiTestHarness.CreateBoardAsync(client, "export-schema-board");

        var response = await client.GetAsync($"/api/metrics/boards/{board.Id}/export");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("# schema_version=1.0");
        content.Should().Contain($"# board_id={board.Id}");
    }

    [Fact]
    public async Task ExportCsv_ShouldContainAllSections()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "export-sections");
        var board = await ApiTestHarness.CreateBoardAsync(client, "export-sections-board");

        var response = await client.GetAsync($"/api/metrics/boards/{board.Id}/export");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("[Summary]");
        content.Should().Contain("[Throughput]");
        content.Should().Contain("[CycleTime]");
        content.Should().Contain("[WIP]");
        content.Should().Contain("[Blocked]");
    }

    [Fact]
    public async Task ExportCsv_ShouldReturnForbiddenOrNotFound_ForOtherUsersBoard()
    {
        using var ownerClient = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(ownerClient, "export-board-owner");
        var board = await ApiTestHarness.CreateBoardAsync(ownerClient, "export-private");

        using var outsiderClient = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(outsiderClient, "export-outsider");

        var response = await outsiderClient.GetAsync($"/api/metrics/boards/{board.Id}/export");

        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(response);
    }

    [Fact]
    public async Task ExportCsv_NonExistentBoard_ShouldReturnNotFoundOrForbidden()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "export-notfound");

        var response = await client.GetAsync($"/api/metrics/boards/{Guid.NewGuid()}/export");

        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(response);
    }

    [Fact]
    public async Task ExportCsv_WithDateRange_ShouldRespectFilters()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "export-range");
        var board = await ApiTestHarness.CreateBoardAsync(client, "export-range-board");

        var from = DateTimeOffset.UtcNow.AddDays(-7).ToString("o");
        var to = DateTimeOffset.UtcNow.ToString("o");

        var response = await client.GetAsync(
            $"/api/metrics/boards/{board.Id}/export?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("# schema_version=1.0");
        content.Should().Contain("# from=");
        content.Should().Contain("# to=");
    }

    [Fact]
    public async Task ExportCsv_EmptyBoard_ShouldReturnValidCsv()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "export-empty");
        var board = await ApiTestHarness.CreateBoardAsync(client, "export-empty-board");

        var response = await client.GetAsync($"/api/metrics/boards/{board.Id}/export");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "CSV export for an empty board should succeed, not error");
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("[Summary]");
        content.Should().Contain("TotalThroughput,0");
    }
}
