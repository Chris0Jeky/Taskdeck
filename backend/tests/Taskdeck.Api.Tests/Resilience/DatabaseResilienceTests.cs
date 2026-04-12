using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Xunit;

namespace Taskdeck.Api.Tests.Resilience;

/// <summary>
/// Tests that database operations produce appropriate error responses and
/// that health endpoints report database status accurately.
/// </summary>
public class DatabaseResilienceTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public DatabaseResilienceTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    // ── Health Endpoint Reports Database Status ───────────────────────

    [Fact]
    public async Task ReadyCheck_IncludesDatabaseCheck_WhenDatabaseIsReachable()
    {
        var response = await _client.GetAsync("/health/ready");

        // With a working DB, ready check may be OK or 503 depending on worker state,
        // but the database check itself should be Healthy.
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.TryGetProperty("checks", out var checks).Should().BeTrue();

        var database = checks.GetProperty("database");
        database.GetProperty("status").GetString().Should().Be("Healthy",
            "database check should report Healthy when database is reachable");
    }

    [Fact]
    public async Task LiveCheck_AlwaysReturnsHealthy_RegardlessOfDatabaseState()
    {
        var response = await _client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "liveness probe should always return 200");

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("status").GetString().Should().Be("Healthy",
            "live check is a simple heartbeat, independent of database state");
    }

    // ── Database Error Handling in API Operations ──────────────────────

    [Fact]
    public async Task Operations_OnNonExistentResource_ReturnNotFoundInsteadOfCrash()
    {
        await ApiTestHarness.AuthenticateAsync(_client, "db-resilience-notfound");

        // Accessing a non-existent board should return 404, not 500.
        var response = await _client.GetAsync($"/api/boards/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "non-existent resource should return 404, not a database crash");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("errorCode", out var errorCode).Should().BeTrue(
            "404 response should follow error contract");
        errorCode.GetString().Should().Be("NotFound");
    }

    [Fact]
    public async Task ConcurrentWrites_HandleConflictsGracefully()
    {
        await ApiTestHarness.AuthenticateAsync(_client, "db-resilience-conflict");

        // Create a board first.
        var board = await ApiTestHarness.CreateBoardAsync(_client, "db-conflict-board");

        // Try to delete the same board twice in quick succession.
        var delete1 = _client.DeleteAsync($"/api/boards/{board.Id}");
        var delete2 = _client.DeleteAsync($"/api/boards/{board.Id}");

        var results = await Task.WhenAll(delete1, delete2);

        // One should succeed (204/200), the other should get 404.
        // Neither should be 500.
        foreach (var result in results)
        {
            var statusCode = (int)result.StatusCode;
            statusCode.Should().NotBe(500,
                "concurrent operations should not cause unhandled 500 errors");
        }

        var statusCodes = results.Select(r => (int)r.StatusCode).OrderBy(s => s).ToArray();
        statusCodes.Should().Contain(s => s >= 200 && s < 300,
            "at least one delete should succeed");
    }

    // ── Database Write Validation ─────────────────────────────────────

    [Fact]
    public async Task CreateBoard_WithInvalidData_ReturnsValidationError()
    {
        await ApiTestHarness.AuthenticateAsync(_client, "db-resilience-validation");

        // An empty board name should return a validation error, not a DB crash.
        var response = await _client.PostAsJsonAsync(
            "/api/boards",
            new CreateBoardDto("", "Empty name board"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "invalid data should return 400, not a database crash");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("errorCode", out _).Should().BeTrue(
            "400 response should follow the error contract");
    }
}
