using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Taskdeck.Api.Extensions;
using Taskdeck.Api.Tests.Support;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Guards the routing boundary between the machine-facing surfaces (REST API, SignalR hubs, health
/// probes, MCP transport) and the SPA fallback (#1971).
///
/// These tests run against <see cref="SpaShellTestWebApplicationFactory"/>, which supplies a real
/// web root containing an index.html. That matters: the plain test host ships no wwwroot, so
/// <c>MapFallbackToFile</c> answers every unmatched path with an empty 404 there and would hide the
/// defect entirely. With the shell present the host matches the deployed shape, where the unscoped
/// fallback used to return <c>200 OK</c> + index.html for any unknown <c>/api/*</c> path.
/// </summary>
public class SpaFallbackRoutingApiTests : IClassFixture<SpaShellTestWebApplicationFactory>
{
    private readonly SpaShellTestWebApplicationFactory _factory;

    public SpaFallbackRoutingApiTests(SpaShellTestWebApplicationFactory factory) => _factory = factory;

    public static TheoryData<string> UnknownNonSpaPaths() =>
    [
        // The exact path from the 2026-08-22 horizon run that returned 200 + index.html.
        "/api/definitely-not-a-real-endpoint-hzn",
        // Plausible-looking API paths that do not exist (the real one is /api/automation/proposals).
        "/api/proposals",
        "/api/captures",
        "/api/workspace/review",
        // A file-looking segment: the SPA catch-all is {*path:nonfile}, so this never matched the
        // shell — it fell through as a bodyless 404. It must now carry the API error contract too.
        "/api/nonexistent.json",
        "/hubs/definitely-not-a-real-hub",
        "/health/definitely-not-a-real-probe"
    ];

    public static TheoryData<string> BareNonSpaPrefixes() =>
    [
        "/api",
        "/hubs",
        "/health"
    ];

    [Theory]
    [MemberData(nameof(UnknownNonSpaPaths))]
    public async Task UnknownNonSpaPath_Returns404ErrorContract_WhenAuthenticated(string path)
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "spa_fallback_auth");

        var response = await client.GetAsync(path);

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.NotFound, "NotFound");
    }

    [Theory]
    [MemberData(nameof(UnknownNonSpaPaths))]
    public async Task UnknownNonSpaPath_Returns404ErrorContract_WhenAnonymous(string path)
    {
        using var client = _factory.CreateClient();

        // An unknown path has no resource to protect, so the AllowAnonymous opt-out on the
        // per-prefix fallbacks must keep the global FallbackPolicy from masking it as a 401.
        var response = await client.GetAsync(path);

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.NotFound, "NotFound");
    }

    [Theory]
    [MemberData(nameof(BareNonSpaPrefixes))]
    public async Task BareNonSpaPrefix_Returns404ErrorContract(string prefix)
    {
        using var client = _factory.CreateClient();

        // "/api" itself is not an endpoint and is not a client-side route either: the catch-all
        // fallback pattern "{prefix}/{**path}" matches the bare prefix with an empty path segment.
        var response = await client.GetAsync(prefix);

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.NotFound, "NotFound");
    }

    [Theory]
    [InlineData("/api/definitely-not-a-real-endpoint-hzn")]
    [InlineData("/hubs/definitely-not-a-real-hub")]
    [InlineData("/health/definitely-not-a-real-probe")]
    public async Task UnknownNonSpaPath_NeverReturnsTheSpaShell(string path)
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();

        response.Content.Headers.ContentType?.MediaType.Should().NotBe("text/html");
        body.Should().NotContain(
            SpaShellTestWebApplicationFactory.ShellMarker,
            "a machine-facing path must never be answered with the app shell (#1971)");
    }

    [Theory]
    // The deep client-side routes named in the acceptance criteria, plus a route that does not
    // exist client-side either — Vue Router owns the in-app 404, so the shell must still load.
    [InlineData("/workspace/review")]
    [InlineData("/workspace/boards/1f2e3d4c-5b6a-4798-8899-aabbccddeeff")]
    [InlineData("/login")]
    [InlineData("/some/client/route")]
    public async Task ClientSideRoute_StillServesSpaShell(string path)
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
        body.Should().Contain(SpaShellTestWebApplicationFactory.ShellMarker);
    }

    [Fact]
    public async Task BareRoot_StillServesSpaShell()
    {
        using var client = _factory.CreateClient();

        // The explicit "/" endpoint added for #1181 — the {*path:nonfile} catch-all never matched
        // the empty path, and the new per-prefix fallbacks must not disturb it.
        var response = await client.GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
        body.Should().Contain(SpaShellTestWebApplicationFactory.ShellMarker);
    }

    [Fact]
    public async Task ExistingApiRoute_StillReturns401_WhenAnonymous()
    {
        using var client = _factory.CreateClient();

        // Ordering guard (#1132 AC4): real routes are non-fallback endpoints and win the match, so
        // the new 404 fallbacks must not downgrade an auth failure on an EXISTING route to a 404.
        var response = await client.GetAsync("/api/boards");

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.Unauthorized, "Unauthorized");
    }

    [Fact]
    public async Task ExistingApiRoute_StillSucceeds_WhenAuthenticated()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "spa_fallback_existing");

        var response = await client.GetAsync("/api/boards");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ExistingHealthRoute_StillReturns200_WhenAnonymous()
    {
        using var client = _factory.CreateClient();

        // /health/live is the probe path deploy configs use; scoping the fallback must not touch it.
        var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UnknownMcpPath_StillReturns401_WithoutApiKey()
    {
        using var client = _factory.CreateClient();

        // ApiKeyMiddleware gates the whole /mcp prefix before routing, so the AllowAnonymous on the
        // /mcp 404 fallback cannot open an unauthenticated window onto the MCP surface.
        var response = await client.GetAsync("/mcp/definitely-not-a-real-mcp-path");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public void NonSpaPathPrefixes_CoverEveryMachineFacingSurface()
    {
        // Pins the declared prefix set so adding a machine-facing surface without extending it is a
        // deliberate edit rather than a silent regression back to 200 + index.html.
        PipelineConfiguration.NonSpaPathPrefixes.Should().BeEquivalentTo(
            ["/api", "/hubs", "/health", "/mcp"]);
    }
}

/// <summary>
/// A <see cref="TestWebApplicationFactory"/> whose host serves a real SPA shell from a temporary web
/// root. The repository ships no <c>backend/src/Taskdeck.Api/wwwroot</c> (the Vue build output is
/// copied in at package time), so without this the static-file middleware and
/// <c>MapFallbackToFile</c> have nothing to serve and every fallback path answers 404 — which would
/// make the #1971 tests pass against the unfixed pipeline.
/// </summary>
public sealed class SpaShellTestWebApplicationFactory : TestWebApplicationFactory
{
    /// <summary>Marker text present only in the stand-in shell, never in an API error body.</summary>
    internal const string ShellMarker = "taskdeck-spa-shell-under-test";

    private readonly string _webRootPath;

    public SpaShellTestWebApplicationFactory()
    {
        // Created before the host builds: the static-file middleware resolves WebRootFileProvider at
        // startup and falls back to a NullFileProvider when the directory is missing.
        _webRootPath = Path.Combine(Path.GetTempPath(), $"taskdeck-spa-webroot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_webRootPath);
        File.WriteAllText(
            Path.Combine(_webRootPath, "index.html"),
            $"<!doctype html><html lang=\"en\"><head><title>{ShellMarker}</title></head>" +
            "<body><div id=\"app\"></div></body></html>");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseWebRoot(_webRootPath);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        try
        {
            Directory.Delete(_webRootPath, recursive: true);
        }
        catch (IOException)
        {
            // Cleanup failure must not fail test teardown.
        }
    }
}
