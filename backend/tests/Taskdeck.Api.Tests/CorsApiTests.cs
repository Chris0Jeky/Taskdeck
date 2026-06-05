using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Taskdeck.Api.Tests.Support;
using Xunit;

namespace Taskdeck.Api.Tests;

public class CorsApiTests : IClassFixture<TestWebApplicationFactory>
{
    private const string AccessControlAllowOriginHeader = "Access-Control-Allow-Origin";
    private const string AccessControlAllowCredentialsHeader = "Access-Control-Allow-Credentials";
    private const string DefaultFrontendOrigin = "http://localhost:5173";

    private readonly TestWebApplicationFactory _baseFactory;

    public CorsApiTests(TestWebApplicationFactory baseFactory)
    {
        _baseFactory = baseFactory;
    }

    [Fact]
    public async Task Cors_ShouldAllowDefaultFrontendOrigin()
    {
        using var client = _baseFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.TryAddWithoutValidation("Origin", DefaultFrontendOrigin);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues(AccessControlAllowOriginHeader, out var allowedOrigins).Should().BeTrue();
        allowedOrigins.Should().ContainSingle().Which.Should().Be(DefaultFrontendOrigin);
    }

    [Fact]
    public async Task Cors_ShouldAllowDevelopmentConfiguredAlternateOrigin()
    {
        const string alternateOrigin = "http://localhost:5189";
        using var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Cors:DevelopmentAllowedOrigins:0", alternateOrigin);
        });
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.TryAddWithoutValidation("Origin", alternateOrigin);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues(AccessControlAllowOriginHeader, out var allowedOrigins).Should().BeTrue();
        allowedOrigins.Should().ContainSingle().Which.Should().Be(alternateOrigin);
    }

    [Theory]
    [InlineData("http://localhost:4173")]
    [InlineData("http://localhost:5001")]
    public async Task Cors_ShouldAllowDevelopmentFallbackOrigins(string fallbackOrigin)
    {
        using var client = _baseFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.TryAddWithoutValidation("Origin", fallbackOrigin);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues(AccessControlAllowOriginHeader, out var allowedOrigins).Should().BeTrue();
        allowedOrigins.Should().ContainSingle().Which.Should().Be(fallbackOrigin);
    }

    [Fact]
    public async Task Cors_ShouldNormalizeConfiguredDevelopmentOriginWithPath()
    {
        const string configuredOrigin = "http://localhost:5189/some/path/";
        const string requestOrigin = "http://localhost:5189";
        using var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Cors:DevelopmentAllowedOrigins:0", configuredOrigin);
        });
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.TryAddWithoutValidation("Origin", requestOrigin);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues(AccessControlAllowOriginHeader, out var allowedOrigins).Should().BeTrue();
        allowedOrigins.Should().ContainSingle().Which.Should().Be(requestOrigin);
    }

    [Fact]
    public async Task Cors_ShouldIgnoreDevelopmentConfiguredAlternateOriginOutsideDevelopment()
    {
        const string alternateOrigin = "http://localhost:5189";
        using var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Jwt:SecretKey", ApiTestHarness.ProductionTestJwtSecret);
            builder.UseSetting("Connectors:EncryptionKey", ApiTestHarness.TestEncryptionKey);
            builder.UseSetting("Cors:DevelopmentAllowedOrigins:0", alternateOrigin);
        });
        using var client = factory.CreateClient();

        using var alternateOriginRequest = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        alternateOriginRequest.Headers.TryAddWithoutValidation("Origin", alternateOrigin);
        var alternateOriginResponse = await client.SendAsync(alternateOriginRequest);

        alternateOriginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        alternateOriginResponse.Headers.TryGetValues(AccessControlAllowOriginHeader, out _).Should().BeFalse();

        // Fail-closed (#1132): with no Cors:AllowedOrigins configured in Production, even the
        // default localhost frontend origin is denied (no Access-Control-Allow-Origin header).
        // Previously this fell back to localhost and authorized credentialed cross-origin requests.
        using var defaultOriginRequest = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        defaultOriginRequest.Headers.TryAddWithoutValidation("Origin", DefaultFrontendOrigin);
        var defaultOriginResponse = await client.SendAsync(defaultOriginRequest);

        defaultOriginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        defaultOriginResponse.Headers.TryGetValues(AccessControlAllowOriginHeader, out _).Should().BeFalse();
    }

    [Fact]
    public async Task Cors_ShouldFailClosed_InProduction_WhenNoOriginsConfigured()
    {
        using var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Jwt:SecretKey", ApiTestHarness.ProductionTestJwtSecret);
            builder.UseSetting("Connectors:EncryptionKey", ApiTestHarness.TestEncryptionKey);
        });
        using var client = factory.CreateClient();

        foreach (var origin in new[] { DefaultFrontendOrigin, "https://app.example.com" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
            request.Headers.TryAddWithoutValidation("Origin", origin);
            var response = await client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Headers.TryGetValues(AccessControlAllowOriginHeader, out _)
                .Should().BeFalse($"origin {origin} must be denied when no production origins are configured");
        }
    }

    [Fact]
    public async Task Cors_ShouldAllowConfiguredProductionOrigin()
    {
        const string productionOrigin = "https://app.example.com";
        using var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Jwt:SecretKey", ApiTestHarness.ProductionTestJwtSecret);
            builder.UseSetting("Connectors:EncryptionKey", ApiTestHarness.TestEncryptionKey);
            builder.UseSetting("Cors:AllowedOrigins:0", productionOrigin);
        });
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.TryAddWithoutValidation("Origin", productionOrigin);
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues(AccessControlAllowOriginHeader, out var allowedOrigins).Should().BeTrue();
        allowedOrigins.Should().ContainSingle().Which.Should().Be(productionOrigin);

        // Configuring one origin must not silently reopen the localhost fallback.
        using var localhostRequest = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        localhostRequest.Headers.TryAddWithoutValidation("Origin", DefaultFrontendOrigin);
        var localhostResponse = await client.SendAsync(localhostRequest);
        localhostResponse.Headers.TryGetValues(AccessControlAllowOriginHeader, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("/health/live", "GET", false)]
    [InlineData("/hubs/boards/negotiate?negotiateVersion=1", "POST", false)]
    [InlineData("/hubs/boards/negotiate?negotiateVersion=1", "OPTIONS", true)]
    public async Task Cors_ShouldIncludeAllowCredentials_ForAllowedOriginRequests(
        string path, string methodName, bool isPreflight)
    {
        using var client = _baseFactory.CreateClient();
        var method = new HttpMethod(methodName);
        using var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation("Origin", DefaultFrontendOrigin);

        if (isPreflight)
        {
            request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "POST");
            request.Headers.TryAddWithoutValidation("Access-Control-Request-Headers", "authorization");
        }
        else if (method == HttpMethod.Post)
        {
            request.Content = new StringContent(string.Empty);
        }

        var response = await client.SendAsync(request);

        response.Headers.TryGetValues(AccessControlAllowOriginHeader, out var allowedOrigins).Should().BeTrue();
        allowedOrigins.Should().ContainSingle().Which.Should().Be(DefaultFrontendOrigin);
        response.Headers.TryGetValues(AccessControlAllowCredentialsHeader, out var credentialsValues).Should().BeTrue();
        credentialsValues.Should().ContainSingle().Which.Should().Be("true");
    }

    [Theory]
    [InlineData("http://localhost:4173")]
    [InlineData("http://localhost:5001")]
    public async Task Cors_ShouldRejectDevelopmentFallbackOriginsOutsideDevelopment(string fallbackOrigin)
    {
        using var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Jwt:SecretKey", ApiTestHarness.ProductionTestJwtSecret);
            builder.UseSetting("Connectors:EncryptionKey", ApiTestHarness.TestEncryptionKey);
        });
        using var client = factory.CreateClient();

        using var fallbackOriginRequest = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        fallbackOriginRequest.Headers.TryAddWithoutValidation("Origin", fallbackOrigin);
        var fallbackOriginResponse = await client.SendAsync(fallbackOriginRequest);

        fallbackOriginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        fallbackOriginResponse.Headers.TryGetValues(AccessControlAllowOriginHeader, out _).Should().BeFalse();
    }
}
