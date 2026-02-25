using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace Taskdeck.Api.Tests;

public class CorsApiTests : IClassFixture<TestWebApplicationFactory>
{
    private const string AccessControlAllowOriginHeader = "Access-Control-Allow-Origin";
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

    [Fact]
    public async Task Cors_ShouldIgnoreDevelopmentConfiguredAlternateOriginOutsideDevelopment()
    {
        const string alternateOrigin = "http://localhost:5189";
        using var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Cors:DevelopmentAllowedOrigins:0", alternateOrigin);
        });
        using var client = factory.CreateClient();

        using var alternateOriginRequest = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        alternateOriginRequest.Headers.TryAddWithoutValidation("Origin", alternateOrigin);
        var alternateOriginResponse = await client.SendAsync(alternateOriginRequest);

        alternateOriginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        alternateOriginResponse.Headers.TryGetValues(AccessControlAllowOriginHeader, out _).Should().BeFalse();

        using var defaultOriginRequest = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        defaultOriginRequest.Headers.TryAddWithoutValidation("Origin", DefaultFrontendOrigin);
        var defaultOriginResponse = await client.SendAsync(defaultOriginRequest);

        defaultOriginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        defaultOriginResponse.Headers.TryGetValues(AccessControlAllowOriginHeader, out var allowedOrigins).Should().BeTrue();
        allowedOrigins.Should().ContainSingle().Which.Should().Be(DefaultFrontendOrigin);
    }
}
