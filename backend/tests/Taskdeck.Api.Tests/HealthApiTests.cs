using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
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
    }
}
