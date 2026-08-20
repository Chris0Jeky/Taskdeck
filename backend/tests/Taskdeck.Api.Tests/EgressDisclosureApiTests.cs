using System.Net;
using System.Text.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Xunit;

namespace Taskdeck.Api.Tests;

public class EgressDisclosureApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public EgressDisclosureApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetDisclosure_ShouldReturnUnauthorized_WhenNoToken()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/privacy/egress");

        await ApiTestHarness.AssertUnauthorizedAsync(response);
    }

    [Fact]
    public async Task GetDisclosure_ShouldReturnOk_ForAuthenticatedUser()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "egress-disclosure");

        var response = await client.GetAsync("/api/privacy/egress");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("totalCount").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        doc.RootElement.GetProperty("destinations").GetArrayLength().Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task GetDisclosure_ShouldContainLlmProvider()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "egress-llm");

        var response = await client.GetAsync("/api/privacy/egress");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var destinations = doc.RootElement.GetProperty("destinations");

        var openaiEntry = destinations.EnumerateArray()
            .FirstOrDefault(d => d.GetProperty("host").GetString() == "api.openai.com");
        openaiEntry.ValueKind.Should().NotBe(JsonValueKind.Undefined, "OpenAI should be in egress disclosure");
        openaiEntry.GetProperty("toolOrAgent").GetString().Should().Be("OpenAiLlmProvider");
        openaiEntry.GetProperty("dataClassification").GetString().Should().Be("UserContent");
        openaiEntry.GetProperty("payloadCategory").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetDisclosure_ShouldNotContainRetiredGeminiProvider()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "egress-retired-provider");

        var response = await client.GetAsync("/api/privacy/egress");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var destinations = doc.RootElement.GetProperty("destinations");

        var retiredEntry = destinations.EnumerateArray()
            .FirstOrDefault(d => d.GetProperty("host").GetString() == "generativelanguage.googleapis.com");
        retiredEntry.ValueKind.Should().Be(JsonValueKind.Undefined);
    }

    [Fact]
    public async Task GetDisclosure_DestinationsHaveRequiredFields()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "egress-fields");

        var response = await client.GetAsync("/api/privacy/egress");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var destinations = doc.RootElement.GetProperty("destinations");

        foreach (var dest in destinations.EnumerateArray())
        {
            dest.TryGetProperty("host", out _).Should().BeTrue();
            dest.TryGetProperty("payloadCategory", out _).Should().BeTrue();
            dest.TryGetProperty("toolOrAgent", out _).Should().BeTrue();
            dest.TryGetProperty("dataClassification", out _).Should().BeTrue();
            dest.GetProperty("host").GetString().Should().NotBeNullOrWhiteSpace();
            dest.GetProperty("payloadCategory").GetString().Should().NotBeNullOrWhiteSpace();
            dest.GetProperty("toolOrAgent").GetString().Should().NotBeNullOrWhiteSpace();
            dest.GetProperty("dataClassification").GetString().Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task GetDisclosure_TotalCountMatchesArrayLength()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "egress-count");

        var response = await client.GetAsync("/api/privacy/egress");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var totalCount = doc.RootElement.GetProperty("totalCount").GetInt32();
        var arrayLength = doc.RootElement.GetProperty("destinations").GetArrayLength();
        totalCount.Should().Be(arrayLength);
    }

    [Fact]
    public async Task GetDisclosure_ShouldContainSentryEntry()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "egress-sentry");

        var response = await client.GetAsync("/api/privacy/egress");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var destinations = doc.RootElement.GetProperty("destinations");

        var sentryEntry = destinations.EnumerateArray()
            .FirstOrDefault(d => d.GetProperty("host").GetString()?.Contains("sentry") == true);
        sentryEntry.ValueKind.Should().NotBe(JsonValueKind.Undefined, "Sentry should be in egress disclosure");
        sentryEntry.GetProperty("dataClassification").GetString().Should().Be("MetadataOnly");
    }

    [Fact]
    public async Task GetDisclosure_NoSecretOrCredentialDataExposed()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "egress-nosecret");

        var response = await client.GetAsync("/api/privacy/egress");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var destinations = doc.RootElement.GetProperty("destinations");

        foreach (var dest in destinations.EnumerateArray())
        {
            var host = dest.GetProperty("host").GetString()!;
            var tool = dest.GetProperty("toolOrAgent").GetString()!;

            host.Should().NotMatchRegex(@"sk-[A-Za-z0-9]{20,}",
                $"host field for {tool} must not contain an API key pattern");
            host.Should().NotMatchRegex(@"[A-Za-z0-9+/]{40,}={0,2}",
                $"host field for {tool} must not contain a base64 credential");
            tool.Should().NotContainEquivalentOf("password",
                "toolOrAgent must not reference passwords");
            tool.Should().NotContainEquivalentOf("secret",
                "toolOrAgent must not reference secrets");
        }

        body.Should().NotContainEquivalentOf("Bearer ",
            "Bearer tokens must not appear in disclosure");
        body.Should().NotMatchRegex(@"sk-[A-Za-z0-9]{20,}",
            "OpenAI-style API keys must not appear in disclosure");
        body.Should().NotMatchRegex(@"ghp_[A-Za-z0-9]{36}",
            "GitHub PATs must not appear in disclosure");
    }

    [Fact]
    public async Task GetDisclosure_GitHubConnectorIsRegistered()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "egress-github");

        var response = await client.GetAsync("/api/privacy/egress");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var destinations = doc.RootElement.GetProperty("destinations");

        var githubEntry = destinations.EnumerateArray()
            .FirstOrDefault(d => d.GetProperty("host").GetString() == "api.github.com");
        githubEntry.ValueKind.Should().NotBe(JsonValueKind.Undefined,
            "GitHub connector should register its egress destination");
    }
}
