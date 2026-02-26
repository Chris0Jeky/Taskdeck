using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace Taskdeck.Api.Tests;

public class SecurityHeadersApiTests : IClassFixture<TestWebApplicationFactory>
{
    private const string ReferrerPolicyHeaderName = "Referrer-Policy";

    private readonly TestWebApplicationFactory _factory;

    public SecurityHeadersApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SecurityHeaders_ShouldBePresent_OnSuccessfulRequests()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues(HeaderNames.XFrameOptions, out var xFrameOptions).Should().BeTrue();
        xFrameOptions.Should().ContainSingle().Which.Should().Be("DENY");
        response.Headers.TryGetValues(HeaderNames.XContentTypeOptions, out var xContentTypeOptions).Should().BeTrue();
        xContentTypeOptions.Should().ContainSingle().Which.Should().Be("nosniff");
        response.Headers.TryGetValues(ReferrerPolicyHeaderName, out var referrerPolicy).Should().BeTrue();
        referrerPolicy.Should().ContainSingle().Which.Should().Be("no-referrer");
        response.Headers.TryGetValues("Content-Security-Policy", out var cspValues).Should().BeTrue();
        cspValues.Should().ContainSingle();
    }

    [Fact]
    public async Task SecurityHeaders_ShouldBePresent_OnUnauthorizedResponses()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/boards");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.TryGetValues(HeaderNames.XFrameOptions, out _).Should().BeTrue();
        response.Headers.TryGetValues(HeaderNames.XContentTypeOptions, out _).Should().BeTrue();
        response.Headers.TryGetValues(ReferrerPolicyHeaderName, out _).Should().BeTrue();
        response.Headers.TryGetValues("Content-Security-Policy", out _).Should().BeTrue();
    }

    [Fact]
    public async Task SecurityHeaders_ShouldNotEmitHsts_ForHttpRequestsInDevelopment()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains(HeaderNames.StrictTransportSecurity).Should().BeFalse();
    }

    [Fact]
    public async Task SecurityHeaders_ShouldEmitHsts_ForHttpsRequestsOutsideDevelopment()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues(HeaderNames.StrictTransportSecurity, out var hstsValues).Should().BeTrue();
        hstsValues.Should().ContainSingle().Which.Should().Contain("max-age=");
    }
}
