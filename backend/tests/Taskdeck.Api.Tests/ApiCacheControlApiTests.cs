using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Taskdeck.Api.Middleware;
using Taskdeck.Api.Tests.Support;
using Xunit;

namespace Taskdeck.Api.Tests;

public class ApiCacheControlApiTests : IClassFixture<TestWebApplicationFactory>
{
    private const string ExpectedCacheControl = "no-store, private";
    private readonly TestWebApplicationFactory _factory;

    public ApiCacheControlApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ApiResponses_StayPrivate_WhenOptionalSecurityHeadersAreDisabled()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.UseSetting("SecurityHeaders:Enabled", "false"));
        using var client = factory.CreateClient();

        var success = await client.GetAsync("/api/auth/providers");
        var unauthorized = await client.GetAsync("/api/boards");
        var notFound = await client.GetAsync("/api/no-such-endpoint");

        success.StatusCode.Should().Be(HttpStatusCode.OK);
        unauthorized.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        notFound.StatusCode.Should().Be(HttpStatusCode.NotFound);
        AssertPrivate(success);
        AssertPrivate(unauthorized);
        AssertPrivate(notFound);
    }

    [Fact]
    public async Task CrossUserForbiddenApiResponse_StaysPrivate()
    {
        using var owner = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(owner, "api-cache-owner");
        var board = await ApiTestHarness.CreateBoardAsync(owner, stem: "api-cache-private");

        using var otherUser = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(otherUser, "api-cache-other");

        var response = await otherUser.GetAsync($"/api/boards/{board.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        AssertPrivate(response);
    }

    [Fact]
    public async Task UnhandledApiError_StaysPrivate()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/throws";
        context.Response.Body = new MemoryStream();
        var exceptionHandler = new UnhandledExceptionMiddleware(
            _ => throw new InvalidOperationException("test failure"),
            NullLogger<UnhandledExceptionMiddleware>.Instance);
        var middleware = new ApiCacheControlMiddleware(exceptionHandler.InvokeAsync);

        await middleware.InvokeAsync(context);
        await context.Response.StartAsync();

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        context.Response.Headers["Cache-Control"].ToString().Should().Be(ExpectedCacheControl);
    }

    private static void AssertPrivate(HttpResponseMessage response)
    {
        response.Headers.TryGetValues("Cache-Control", out var values).Should().BeTrue();
        values.Should().ContainSingle().Which.Should().Be(ExpectedCacheControl);
    }
}
