using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Xunit;

namespace Taskdeck.Api.Tests;

public class AutomationMetricsControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public AutomationMetricsControllerTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetCohortMetrics_Unauthenticated_ShouldReturn401()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/automation/metrics/cohorts");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetCohortMetrics_ValidRequest_ShouldReturn200WithEmptyCohorts()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "cohort-ok");

        var response = await client.GetAsync("/api/automation/metrics/cohorts");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CohortResponse>();
        body.Should().NotBeNull();
        body!.Cohorts.Should().BeEmpty();
        body.DateRange.Should().NotBeNull();
    }

    [Fact]
    public async Task GetCohortMetrics_FromAfterTo_ShouldReturn400()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "cohort-badrange");

        var from = DateTimeOffset.UtcNow.ToString("o");
        var to = DateTimeOffset.UtcNow.AddDays(-7).ToString("o");

        var response = await client.GetAsync(
            $"/api/automation/metrics/cohorts?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}");

        await ApiTestHarness.AssertErrorContractAsync(
            response,
            HttpStatusCode.BadRequest,
            "ValidationError");
    }

    [Fact]
    public async Task GetCohortMetrics_RangeExceeds365Days_ShouldReturn400()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "cohort-longrange");

        var from = DateTimeOffset.UtcNow.AddDays(-400).ToString("o");
        var to = DateTimeOffset.UtcNow.ToString("o");

        var response = await client.GetAsync(
            $"/api/automation/metrics/cohorts?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}");

        await ApiTestHarness.AssertErrorContractAsync(
            response,
            HttpStatusCode.BadRequest,
            "ValidationError");
    }

    [Fact]
    public async Task GetCohortMetrics_CustomDateRange_ShouldReturn200()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "cohort-custom");

        var from = DateTimeOffset.UtcNow.AddDays(-14).ToString("o");
        var to = DateTimeOffset.UtcNow.ToString("o");

        var response = await client.GetAsync(
            $"/api/automation/metrics/cohorts?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private sealed class CohortResponse
    {
        public List<object> Cohorts { get; set; } = [];
        public DateRangeResponse? DateRange { get; set; }
    }

    private sealed class DateRangeResponse
    {
        public string From { get; set; } = "";
        public string To { get; set; } = "";
    }
}
