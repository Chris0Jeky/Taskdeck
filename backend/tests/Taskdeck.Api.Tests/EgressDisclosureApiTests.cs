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
        doc.RootElement.GetProperty("totalCount").GetInt32().Should().BeGreaterOrEqualTo(1);
        doc.RootElement.GetProperty("destinations").GetArrayLength().Should().BeGreaterOrEqualTo(1);
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
    public async Task GetDisclosure_ShouldContainGeminiProvider()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "egress-gemini");

        var response = await client.GetAsync("/api/privacy/egress");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var destinations = doc.RootElement.GetProperty("destinations");

        var geminiEntry = destinations.EnumerateArray()
            .FirstOrDefault(d => d.GetProperty("host").GetString() == "generativelanguage.googleapis.com");
        geminiEntry.ValueKind.Should().NotBe(JsonValueKind.Undefined, "Gemini should be in egress disclosure");
        geminiEntry.GetProperty("toolOrAgent").GetString().Should().Be("GeminiLlmProvider");
        geminiEntry.GetProperty("dataClassification").GetString().Should().Be("UserContent");
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
        body.Should().NotContain("key", "API keys must not appear in disclosure");
        body.Should().NotContain("secret", "Secrets must not appear in disclosure");
        body.Should().NotContain("password", "Passwords must not appear in disclosure");
        body.Should().NotContain("token", "Tokens must not appear in disclosure");
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
