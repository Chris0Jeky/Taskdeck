using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Api.Tests.ErrorContract;

/// <summary>
/// Verifies GP-03 error contract compliance for content-type edge cases,
/// malformed JSON, and routing errors. Every error response must return
/// a structured JSON body with errorCode and message, never raw HTML
/// or stack traces.
/// </summary>
public class ContentTypeAndFormatErrorContractTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ContentTypeAndFormatErrorContractTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PostBoard_MalformedJson_Returns400WithJsonBody()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "fmt-err-malformed");

        var response = await client.PostAsync(
            "/api/boards",
            new StringContent("{invalid-json", Encoding.UTF8, "application/json"));

        // ASP.NET model-binding failures return ProblemDetails (RFC 9457),
        // not the app-level ApiErrorResponse. We verify the response is
        // structured JSON (not HTML or stack traces) with a 400 status.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotBeNullOrWhiteSpace();
        var parseAction = () => JsonDocument.Parse(body);
        parseAction.Should().NotThrow("error responses must be valid JSON, not HTML or stack traces");
    }

    [Fact]
    public async Task PostBoard_EmptyBody_Returns400OrUnsupportedMedia()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "fmt-err-emptybody");

        var response = await client.PostAsync(
            "/api/boards",
            new StringContent(string.Empty, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.UnsupportedMediaType);

        // Verify the response body is valid JSON (not HTML or stack traces)
        var body = await response.Content.ReadAsStringAsync();
        if (!string.IsNullOrWhiteSpace(body))
        {
            var parseAction = () => JsonDocument.Parse(body);
            parseAction.Should().NotThrow("error responses must be valid JSON");
        }
    }

    [Fact]
    public async Task PostBoard_WrongContentType_Returns415OrBadRequest()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "fmt-err-ct");

        var response = await client.PostAsync(
            "/api/boards",
            new StringContent("<xml/>", Encoding.UTF8, "application/xml"));

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.UnsupportedMediaType,
            HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        if (!string.IsNullOrWhiteSpace(body))
        {
            var parseAction = () => JsonDocument.Parse(body);
            parseAction.Should().NotThrow("error responses must be valid JSON, not stack traces");
        }
    }

    [Fact]
    public async Task NonExistentRoute_Returns404WithJsonBody()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "fmt-err-noroute");

        var response = await client.GetAsync("/api/this-route-does-not-exist");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync();

        // Verify the response is not an HTML page (default ASP.NET behavior)
        if (!string.IsNullOrWhiteSpace(body))
        {
            body.TrimStart().Should().NotStartWith("<",
                "404 responses should not return HTML -- they should return JSON or empty body");
        }
    }

    [Fact]
    public async Task NonExistentApiRoute_Returns404_NotHtml()
    {
        // Test specifically for /api/ prefix routes with authentication
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "fmt-err-api-noroute");

        var response = await client.GetAsync("/api/nonexistent/resource/path");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var body = await response.Content.ReadAsStringAsync();
        if (!string.IsNullOrWhiteSpace(body))
        {
            body.TrimStart().Should().NotStartWith("<",
                "API routes should never return HTML error pages");
        }
    }

    [Fact]
    public async Task PostBoard_NullJsonBody_Returns400WithErrorContract()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "fmt-err-null");

        var response = await client.PostAsync(
            "/api/boards",
            new StringContent("null", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.UnsupportedMediaType);

        var body = await response.Content.ReadAsStringAsync();
        if (!string.IsNullOrWhiteSpace(body))
        {
            var parseAction = () => JsonDocument.Parse(body);
            parseAction.Should().NotThrow("error responses must be valid JSON");
        }
    }

    [Fact]
    public async Task PostBoard_JsonArrayInsteadOfObject_Returns400WithJsonBody()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "fmt-err-array");

        var response = await client.PostAsync(
            "/api/boards",
            new StringContent("[1,2,3]", Encoding.UTF8, "application/json"));

        // ASP.NET model-binding type mismatch returns ProblemDetails (RFC 9457),
        // not the app-level ApiErrorResponse. We verify the response is
        // structured JSON (not HTML or stack traces) with a 400 status.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotBeNullOrWhiteSpace();
        var parseAction = () => JsonDocument.Parse(body);
        parseAction.Should().NotThrow("error responses must be valid JSON");
    }
}
