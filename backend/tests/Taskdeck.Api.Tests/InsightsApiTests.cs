using System.Net;
using System.Text.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Xunit;

namespace Taskdeck.Api.Tests;

public class InsightsApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public InsightsApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetCohort_ShouldReturnUnauthorized_WhenNoToken()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/insights/cohort");

        await ApiTestHarness.AssertUnauthorizedAsync(response);
    }

    [Fact]
    public async Task GetCohort_ShouldReturnOk_ForAuthenticatedUser()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "insights-cohort");

        var response = await client.GetAsync("/api/insights/cohort");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("acceptedCount").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        doc.RootElement.GetProperty("editedCount").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        doc.RootElement.GetProperty("rejectedCount").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        doc.RootElement.GetProperty("totalCount").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        doc.RootElement.GetProperty("acceptanceRate").GetDouble().Should().BeGreaterThanOrEqualTo(0.0);
    }

    [Fact]
    public async Task GetCohort_TotalCountMatchesSumOfParts()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "insights-sum");

        var response = await client.GetAsync("/api/insights/cohort");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var accepted = doc.RootElement.GetProperty("acceptedCount").GetInt32();
        var edited = doc.RootElement.GetProperty("editedCount").GetInt32();
        var rejected = doc.RootElement.GetProperty("rejectedCount").GetInt32();
        var total = doc.RootElement.GetProperty("totalCount").GetInt32();
        total.Should().Be(accepted + edited + rejected);
    }

    [Fact]
    public async Task GetCohort_AcceptsCustomPeriodDays()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "insights-period");

        var response = await client.GetAsync("/api/insights/cohort?periodDays=7");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("totalCount").GetInt32().Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task GetCohort_AcceptanceRateIsZeroWhenNoOutcomes()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "insights-empty");

        var response = await client.GetAsync("/api/insights/cohort?periodDays=1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var rate = doc.RootElement.GetProperty("acceptanceRate").GetDouble();
        rate.Should().Be(0.0);
    }

    [Fact]
    public async Task GetMetrics_ShouldReturnUnauthorized_WhenNoToken()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/insights/metrics");

        await ApiTestHarness.AssertUnauthorizedAsync(response);
    }

    [Fact]
    public async Task GetMetrics_ShouldReturnOk_WithMetricsArray()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "insights-metrics");

        var response = await client.GetAsync("/api/insights/metrics");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("metrics", out var metrics).Should().BeTrue();
        metrics.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetMetrics_MetricsHaveRequiredFields()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "insights-fields");

        var response = await client.GetAsync("/api/insights/metrics");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var metrics = doc.RootElement.GetProperty("metrics");

        foreach (var metric in metrics.EnumerateArray())
        {
            metric.TryGetProperty("metricName", out _).Should().BeTrue();
            metric.TryGetProperty("bucketedCount", out _).Should().BeTrue();
            metric.TryGetProperty("timePeriodDays", out _).Should().BeTrue();
            metric.TryGetProperty("promptVersion", out _).Should().BeTrue();
            metric.GetProperty("metricName").GetString().Should().NotBeNullOrWhiteSpace();
            metric.GetProperty("bucketedCount").GetInt32().Should().BeGreaterThanOrEqualTo(0);
            metric.GetProperty("timePeriodDays").GetInt32().Should().BeGreaterThan(0);
            metric.GetProperty("promptVersion").GetString().Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task GetMetrics_NoUserContentInResponse()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "insights-privacy");

        var response = await client.GetAsync("/api/insights/metrics");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContainEquivalentOf("Bearer ",
            "Bearer tokens must not appear in insight metrics");
        body.Should().NotMatchRegex(@"sk-[A-Za-z0-9]{20,}",
            "API keys must not appear in insight metrics");
    }

    [Fact]
    public async Task GetCohort_NegativePeriodDaysDefaultsTo30()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "insights-negperiod");

        var response = await client.GetAsync("/api/insights/cohort?periodDays=-5");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
