using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Middleware;
using Taskdeck.Api.RateLimiting;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Unit tests for the MCP pre-authentication FAILURE budget (#1368). The pre-auth IP limiter must
/// only spend budget on requests that fail authentication, must reject an exhausted address before
/// the API-key parse/database lookup, and must key on the trusted client address (socket address by
/// default; the forwarded client only when forwarded-header middleware is wired for a known proxy).
/// </summary>
public sealed class McpAuthenticationRateLimitingMiddlewareTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    // ── (2) Failed attempts exhaust the budget and are rejected before the auth/lookup layer ──

    [Fact]
    public async Task ExhaustedFailureBudget_Rejects429_BeforeReachingAuthLayer()
    {
        using var limiter = new McpAuthenticationAttemptLimiter(new RateLimitPolicySettings(1, 60));
        var authLayerInvocations = 0;
        var middleware = new McpAuthenticationRateLimitingMiddleware(context =>
        {
            // Stand in for ApiKeyMiddleware + the database lookup: increment on every reach, and
            // simulate a rejected credential (the only 401 source on /mcp).
            authLayerInvocations++;
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        });

        var first = CreateMcpContext("203.0.113.7");
        await middleware.InvokeAsync(first, limiter);
        first.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        authLayerInvocations.Should().Be(1, "the first attempt reaches auth, fails, and spends one permit");

        var second = CreateMcpContext("203.0.113.7");
        await middleware.InvokeAsync(second, limiter);

        second.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        authLayerInvocations.Should().Be(1,
            "an address whose failure budget is spent must be rejected before the key parse/database lookup");

        // 429 contract is preserved (matches the endpoint per-key limiter shape). WriteAsJsonAsync
        // appends "; charset=utf-8", so assert on the media type as the integration test does.
        second.Response.ContentType.Should().StartWith("application/json");
        second.Response.Headers["Retry-After"].ToString().Should().NotBeNullOrWhiteSpace();
        second.Response.Headers["X-RateLimit-Policy"].ToString()
            .Should().Be(RateLimitingPolicyNames.McpAuthenticationPerIp);
        var error = await ReadErrorAsync(second);
        error.Should().NotBeNull();
        error!.ErrorCode.Should().Be(ErrorCodes.TooManyRequests);
        error.Message.Should().NotBeNullOrWhiteSpace();
    }

    // ── (3) Successful requests never decrement the failure budget ──

    [Fact]
    public async Task SuccessfulAuthentications_NeverSpendFailureBudget()
    {
        // Budget of 1: if any successful request spent a permit, the next would 429.
        using var limiter = new McpAuthenticationAttemptLimiter(new RateLimitPolicySettings(1, 60));
        var middleware = new McpAuthenticationRateLimitingMiddleware(context =>
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });

        for (var i = 0; i < 25; i++)
        {
            var context = CreateMcpContext("198.51.100.4");
            await middleware.InvokeAsync(context, limiter);
            context.Response.StatusCode.Should().Be(StatusCodes.Status200OK,
                "valid requests must pass through and never be charged the IP failure budget");
        }
    }

    [Fact]
    public async Task FailuresConsumeBudget_ButInterleavedSuccessesDoNot()
    {
        // Budget of 2 failures. A flood of valid requests between the failures must not advance the
        // budget, so exactly the third FAILURE (not any success) is the one rejected pre-auth.
        using var limiter = new McpAuthenticationAttemptLimiter(new RateLimitPolicySettings(2, 60));
        var middleware = new McpAuthenticationRateLimitingMiddleware(context =>
        {
            // The simulated auth outcome is carried on a request header for the fake.
            context.Response.StatusCode = context.Request.Headers.ContainsKey("x-fail")
                ? StatusCodes.Status401Unauthorized
                : StatusCodes.Status200OK;
            return Task.CompletedTask;
        });

        (await Run(middleware, limiter, "192.0.2.20", fail: true)).Should().Be(StatusCodes.Status401Unauthorized);
        for (var i = 0; i < 10; i++)
        {
            (await Run(middleware, limiter, "192.0.2.20", fail: false)).Should().Be(StatusCodes.Status200OK);
        }
        (await Run(middleware, limiter, "192.0.2.20", fail: true)).Should().Be(StatusCodes.Status401Unauthorized);
        // Budget (2) now spent by the two failures; the next attempt is rejected before auth.
        (await Run(middleware, limiter, "192.0.2.20", fail: true)).Should().Be(StatusCodes.Status429TooManyRequests);
    }

    // ── (4) Forwarded headers OFF by default: bucket keyed on the socket address ──

    [Fact]
    public async Task ForwardedHeadersOff_IgnoresXForwardedFor_KeyingOnSocketAddress()
    {
        using var limiter = new McpAuthenticationAttemptLimiter(new RateLimitPolicySettings(1, 60));
        var middleware = new McpAuthenticationRateLimitingMiddleware(context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        });

        // Same socket address, different X-Forwarded-For. Without forwarded-header middleware the XFF
        // is ignored, so both requests share the one socket bucket.
        var first = CreateMcpContext("192.0.2.9", forwardedFor: "1.1.1.1");
        await middleware.InvokeAsync(first, limiter);
        first.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);

        var second = CreateMcpContext("192.0.2.9", forwardedFor: "9.9.9.9");
        await middleware.InvokeAsync(second, limiter);
        second.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests,
            "a spoofed X-Forwarded-For must not let a caller rotate to a fresh bucket when forwarded headers are off");
    }

    // ── (5) Forwarded headers ON with a known proxy: bucket keyed on the forwarded client ──

    [Fact]
    public async Task ForwardedHeadersOn_WithKnownProxy_KeysOnForwardedClient()
    {
        using var limiter = new McpAuthenticationAttemptLimiter(new RateLimitPolicySettings(1, 60));
        var services = new ServiceCollection();
        services.AddLogging(); // ForwardedHeadersMiddleware resolves ILoggerFactory from DI.
        services.AddSingleton(limiter);
        using var provider = services.BuildServiceProvider();
        var pipeline = BuildForwardedPipeline(provider, knownProxy: "192.0.2.9");

        // Client A behind the trusted proxy fails, exhausting only A's forwarded-client bucket.
        var a1 = CreateMcpContext("192.0.2.9", forwardedFor: "1.1.1.1", provider);
        await pipeline(a1);
        a1.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);

        var a2 = CreateMcpContext("192.0.2.9", forwardedFor: "1.1.1.1", provider);
        await pipeline(a2);
        a2.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests,
            "the same forwarded client is limited on its own bucket");

        // Client B behind the SAME proxy keeps an independent budget — not starved by A.
        var b1 = CreateMcpContext("192.0.2.9", forwardedFor: "2.2.2.2", provider);
        await pipeline(b1);
        b1.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized,
            "a different forwarded client behind the same proxy must have an independent failure budget");
    }

    // ── Helpers ──

    private static async Task<int> Run(
        McpAuthenticationRateLimitingMiddleware middleware,
        McpAuthenticationAttemptLimiter limiter,
        string remoteIp,
        bool fail)
    {
        var context = CreateMcpContext(remoteIp);
        if (fail)
        {
            context.Request.Headers["x-fail"] = "1";
        }

        await middleware.InvokeAsync(context, limiter);
        return context.Response.StatusCode;
    }

    private static RequestDelegate BuildForwardedPipeline(IServiceProvider provider, string knownProxy)
    {
        var app = new ApplicationBuilder(provider);
        var options = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor };
        options.KnownProxies.Add(IPAddress.Parse(knownProxy));
        app.UseForwardedHeaders(options);
        app.UseMiddleware<McpAuthenticationRateLimitingMiddleware>();
        app.Run(context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        });
        return app.Build();
    }

    private static DefaultHttpContext CreateMcpContext(
        string remoteIp,
        string? forwardedFor = null,
        IServiceProvider? services = null)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = services ?? EmptyProvider.Value
        };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = McpRoute;
        context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
        if (forwardedFor is not null)
        {
            context.Request.Headers["X-Forwarded-For"] = forwardedFor;
        }

        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<ApiErrorResponse?> ReadErrorAsync(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        return await JsonSerializer.DeserializeAsync<ApiErrorResponse>(context.Response.Body, WebJson);
    }

    private const string McpRoute = "/mcp";

    private static readonly Lazy<IServiceProvider> EmptyProvider =
        new(() => new ServiceCollection().BuildServiceProvider());
}
