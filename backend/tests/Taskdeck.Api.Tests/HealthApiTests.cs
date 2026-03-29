using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Xunit;

namespace Taskdeck.Api.Tests;

public class HealthApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public HealthApiTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Live_ShouldReturnHealthy()
    {
        var response = await _client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("status").GetString().Should().Be("Healthy");
    }

    [Fact]
    public async Task Ready_ShouldReturnPayloadWithChecks()
    {
        var response = await _client.GetAsync("/health/ready");

        (response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.ServiceUnavailable)
            .Should()
            .BeTrue();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.TryGetProperty("checks", out var checks).Should().BeTrue();
        checks.TryGetProperty("database", out _).Should().BeTrue();
        checks.TryGetProperty("queue", out _).Should().BeTrue();
        checks.TryGetProperty("workers", out _).Should().BeTrue();

        var workers = checks.GetProperty("workers");
        var queueWorker = workers.GetProperty("queueToProposal");
        queueWorker.TryGetProperty("stalenessSeconds", out _).Should().BeTrue();
        queueWorker.TryGetProperty("maxStalenessSeconds", out _).Should().BeTrue();

        var housekeepingWorker = workers.GetProperty("proposalHousekeeping");
        housekeepingWorker.TryGetProperty("stalenessSeconds", out _).Should().BeTrue();
        housekeepingWorker.TryGetProperty("maxStalenessSeconds", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Ready_ShouldExcludeCaptureBacklogFromAutomationQueueDepth()
    {
        await ApiTestHarness.AuthenticateAsync(_client, "health-capture-backlog");

        for (var i = 0; i < 3; i++)
        {
            var captureResponse = await _client.PostAsJsonAsync(
                "/api/capture/items",
                new CreateCaptureItemDto(null, $"capture payload {i}"));
            captureResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var response = await _client.GetAsync("/health/ready");
        (response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.ServiceUnavailable)
            .Should()
            .BeTrue();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var queue = payload.GetProperty("checks").GetProperty("queue");
        queue.GetProperty("depth").GetInt32().Should().Be(0);
        queue.GetProperty("captureDepth").GetInt32().Should().BeGreaterThanOrEqualTo(3);
        queue.GetProperty("totalDepth").GetInt32().Should().BeGreaterThanOrEqualTo(3);
    }
}
