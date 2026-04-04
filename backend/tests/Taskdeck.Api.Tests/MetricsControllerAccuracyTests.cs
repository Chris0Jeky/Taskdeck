using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// HTTP-layer accuracy tests for MetricsController.
/// Covers date range validation, filtering, default range behavior,
/// and edge cases not covered by existing MetricsApiTests.
/// Tracking issue: #718 (TST-51)
/// </summary>
public class MetricsControllerAccuracyTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public MetricsControllerAccuracyTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetBoardMetrics_FromAfterTo_ShouldReturnBadRequest()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "metrics-badrange");
        var board = await ApiTestHarness.CreateBoardAsync(client, "metrics-badrange");

        var from = DateTimeOffset.UtcNow.ToString("o");
        var to = DateTimeOffset.UtcNow.AddDays(-7).ToString("o");

        var response = await client.GetAsync(
            $"/api/metrics/boards/{board.Id}?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "from date after to date should be rejected as a validation error");
    }

    [Fact]
    public async Task GetBoardMetrics_WithLabelFilter_ShouldReturnOk()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "metrics-label");
        var board = await ApiTestHarness.CreateBoardAsync(client, "metrics-label");

        var labelId = Guid.NewGuid();
        var response = await client.GetAsync(
            $"/api/metrics/boards/{board.Id}?labelId={labelId}");

        // Should return OK even with a label filter that matches no cards
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "filtering by a non-existent label should return empty metrics, not an error");
    }

    [Fact]
    public async Task GetBoardMetrics_WithLabelFilter_ReturnsEmptyResults()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "metrics-labelfilt");
        var board = await ApiTestHarness.CreateBoardAsync(client, "metrics-labelfilt");

        var labelId = Guid.NewGuid();
        var response = await client.GetAsync(
            $"/api/metrics/boards/{board.Id}?labelId={labelId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("throughput", out var throughput).Should().BeTrue();
        throughput.GetArrayLength().Should().Be(0,
            "empty board with non-existent label filter should return empty throughput array");
    }

    [Fact]
    public async Task GetBoardMetrics_ResponseStructure_ContainsAllExpectedFields()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "metrics-struct");
        var board = await ApiTestHarness.CreateBoardAsync(client, "metrics-structure");

        var response = await client.GetAsync($"/api/metrics/boards/{board.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.TryGetProperty("boardId", out _).Should().BeTrue("response should contain boardId");
        root.TryGetProperty("from", out _).Should().BeTrue("response should contain from date");
        root.TryGetProperty("to", out _).Should().BeTrue("response should contain to date");
        root.TryGetProperty("throughput", out _).Should().BeTrue("response should contain throughput");
        root.TryGetProperty("averageCycleTimeDays", out _).Should().BeTrue("response should contain averageCycleTimeDays");
        root.TryGetProperty("cycleTimeEntries", out _).Should().BeTrue("response should contain cycleTimeEntries");
        root.TryGetProperty("wipSnapshots", out _).Should().BeTrue("response should contain wipSnapshots");
        root.TryGetProperty("totalWip", out _).Should().BeTrue("response should contain totalWip");
        root.TryGetProperty("blockedCount", out _).Should().BeTrue("response should contain blockedCount");
        root.TryGetProperty("blockedCards", out _).Should().BeTrue("response should contain blockedCards");
    }

    [Fact]
    public async Task GetBoardMetrics_EmptyBoardId_ShouldReturnBadRequest()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "metrics-emptyid");

        // Empty GUID as board ID — should fail validation
        var response = await client.GetAsync(
            $"/api/metrics/boards/{Guid.Empty}");

        // Service returns ValidationError for empty board ID, which maps to 400
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "empty board ID should be rejected as a validation error");
    }

    [Fact]
    public async Task GetBoardMetrics_FromEqualsTo_ShouldReturnOk()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "metrics-eqrange");
        var board = await ApiTestHarness.CreateBoardAsync(client, "metrics-eqrange");

        var now = DateTimeOffset.UtcNow.ToString("o");
        var response = await client.GetAsync(
            $"/api/metrics/boards/{board.Id}?from={Uri.EscapeDataString(now)}&to={Uri.EscapeDataString(now)}");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "from date equal to to date should be valid (empty range)");
    }

    [Fact]
    public async Task GetBoardMetrics_VeryWideRange_ShouldReturnOk()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "metrics-wide");
        var board = await ApiTestHarness.CreateBoardAsync(client, "metrics-widerange");

        var from = DateTimeOffset.UtcNow.AddDays(-365).ToString("o");
        var to = DateTimeOffset.UtcNow.ToString("o");

        var response = await client.GetAsync(
            $"/api/metrics/boards/{board.Id}?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "a 1-year date range should be valid");
    }

    [Fact]
    public async Task GetBoardMetrics_NoParams_DefaultsTo30DayRange()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "metrics-noparams");
        var board = await ApiTestHarness.CreateBoardAsync(client, "metrics-noparams");

        var response = await client.GetAsync($"/api/metrics/boards/{board.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);

        var fromDate = DateTimeOffset.Parse(doc.RootElement.GetProperty("from").GetString()!);
        var toDate = DateTimeOffset.Parse(doc.RootElement.GetProperty("to").GetString()!);

        var daysDifference = (toDate - fromDate).TotalDays;
        daysDifference.Should().BeApproximately(30, 1,
            "default range should be approximately 30 days");
    }

    [Fact]
    public async Task GetBoardMetrics_OnlyFromParam_ToDefaultsToNow()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "metrics-onlyfrom");
        var board = await ApiTestHarness.CreateBoardAsync(client, "metrics-onlyfrom");

        var from = DateTimeOffset.UtcNow.AddDays(-14).ToString("o");
        var beforeRequest = DateTimeOffset.UtcNow;

        var response = await client.GetAsync(
            $"/api/metrics/boards/{board.Id}?from={Uri.EscapeDataString(from)}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);

        var toDate = DateTimeOffset.Parse(doc.RootElement.GetProperty("to").GetString()!);
        // `to` should default to approximately now
        (toDate - beforeRequest).TotalMinutes.Should().BeLessThan(2,
            "when only 'from' is provided, 'to' should default to approximately now");
    }

    [Fact]
    public async Task GetBoardMetrics_ImportedBoardWithColumns_WipSnapshotsReflectColumns()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "metrics-import");

        // Import a board with specific columns and cards
        var importResponse = await client.PostAsJsonAsync(
            "/api/import/boards",
            new ImportBoardDto(
                $"metrics-import-{Guid.NewGuid():N}",
                null,
                new[]
                {
                    new ImportColumnDto("To Do", 0, null),
                    new ImportColumnDto("In Progress", 1, 3),
                    new ImportColumnDto("Done", 2, null),
                },
                new[]
                {
                    new ImportCardDto("Task A", null, "To Do", 0, null, null),
                    new ImportCardDto("Task B", null, "To Do", 1, null, null),
                    new ImportCardDto("Task C", null, "In Progress", 0, null, null),
                },
                Array.Empty<ImportLabelDto>()));

        importResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var importResult = await importResponse.Content.ReadFromJsonAsync<ImportResultDto>();
        importResult.Should().NotBeNull();
        importResult!.BoardId.Should().NotBeNull();

        var response = await client.GetAsync($"/api/metrics/boards/{importResult.BoardId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);

        var wipSnapshots = doc.RootElement.GetProperty("wipSnapshots");
        wipSnapshots.GetArrayLength().Should().Be(3, "board has 3 columns");

        // Total WIP should be 3
        doc.RootElement.GetProperty("totalWip").GetInt32().Should().Be(3);
    }
}
