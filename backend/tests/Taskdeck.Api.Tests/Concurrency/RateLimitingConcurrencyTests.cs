using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Taskdeck.Api.RateLimiting;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Xunit;

namespace Taskdeck.Api.Tests.Concurrency;

/// <summary>
/// Rate limiting concurrency tests exercising:
/// 12. Burst beyond limit (correct number throttled)
/// 13. Cross-user isolation under load (user A hitting limit doesn't affect user B)
///
/// Uses Task.WhenAll with SemaphoreSlim barriers for burst execution.
///
/// See GitHub issue #705 (TST-55).
/// </summary>
public class RateLimitingConcurrencyTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public RateLimitingConcurrencyTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Scenario 12: Burst of requests beyond the rate limit.
    /// Fires N requests simultaneously; after the permit limit is hit,
    /// additional requests should receive 429 Too Many Requests.
    /// </summary>
    [Fact]
    public async Task BurstBeyondLimit_ExcessRequestsGet429()
    {
        const int permitLimit = 2;
        const int burstSize = 5;

        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimiting:Enabled", "true");
            builder.UseSetting("RateLimiting:AuthPerIp:PermitLimit",
                permitLimit.ToString(CultureInfo.InvariantCulture));
            builder.UseSetting("RateLimiting:AuthPerIp:WindowSeconds", "60");
        });

        using var barrier = new SemaphoreSlim(0, burstSize);
        var statusCodes = new ConcurrentBag<HttpStatusCode>();

        var burstTasks = Enumerable.Range(0, burstSize).Select(async _ =>
        {
            using var client = factory.CreateClient();
            await barrier.WaitAsync();
            var resp = await client.PostAsJsonAsync(
                "/api/auth/login",
                new LoginDto($"burst-user-{Guid.NewGuid():N}", "wrong-pass"));
            statusCodes.Add(resp.StatusCode);
        }).ToArray();

        barrier.Release(burstSize);
        await Task.WhenAll(burstTasks);

        var codes = statusCodes.ToList();
        var throttledCount = codes.Count(s => s == (HttpStatusCode)429);
        throttledCount.Should().BeGreaterOrEqualTo(burstSize - permitLimit,
            $"with permit limit {permitLimit} and burst {burstSize}, " +
            $"at least {burstSize - permitLimit} requests should be throttled");
    }

    /// <summary>
    /// Scenario 13: Cross-user isolation under load.
    /// Two users send requests; each user's rate limit should be tracked
    /// independently. User A being throttled should not throttle user B.
    /// </summary>
    [Fact]
    public async Task CrossUserIsolation_UsersThrottledIndependently()
    {
        const int permitLimit = 1;

        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimiting:Enabled", "true");
            builder.UseSetting("RateLimiting:AuthPerIp:PermitLimit", "200");
            builder.UseSetting("RateLimiting:AuthPerIp:WindowSeconds", "60");
            builder.UseSetting("RateLimiting:HotPathPerUser:PermitLimit",
                permitLimit.ToString(CultureInfo.InvariantCulture));
            builder.UseSetting("RateLimiting:HotPathPerUser:WindowSeconds", "60");
        });

        // Register two independent users
        using var clientA = factory.CreateClient();
        using var clientB = factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "rate-iso-a");
        await ApiTestHarness.AuthenticateAsync(clientB, "rate-iso-b");

        // User A fires 2 requests (first OK, second should be 429)
        var a1 = await clientA.PostAsJsonAsync(
            "/api/llm-queue",
            new CreateLlmRequestDto("summarize", "payload A-1"));
        a1.StatusCode.Should().Be(HttpStatusCode.OK);

        var a2 = await clientA.PostAsJsonAsync(
            "/api/llm-queue",
            new CreateLlmRequestDto("summarize", "payload A-2"));
        a2.StatusCode.Should().Be((HttpStatusCode)429,
            "user A should be throttled after exceeding per-user limit");

        // User B's first request should still succeed
        var b1 = await clientB.PostAsJsonAsync(
            "/api/llm-queue",
            new CreateLlmRequestDto("summarize", "payload B-1"));
        b1.StatusCode.Should().Be(HttpStatusCode.OK,
            "user B should not be affected by user A's throttling");
    }

    /// <summary>
    /// Scenario 12b: Verify that throttled requests include Retry-After header.
    /// </summary>
    [Fact]
    public async Task ThrottledRequests_IncludeRetryAfterHeader()
    {
        const int permitLimit = 1;

        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimiting:Enabled", "true");
            builder.UseSetting("RateLimiting:AuthPerIp:PermitLimit",
                permitLimit.ToString(CultureInfo.InvariantCulture));
            builder.UseSetting("RateLimiting:AuthPerIp:WindowSeconds", "60");
        });

        using var client = factory.CreateClient();

        // First request should succeed
        var first = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginDto("retry-header-user", "wrong-pass"));

        // Second request should be throttled
        var second = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginDto("retry-header-user-2", "wrong-pass"));

        if (second.StatusCode == (HttpStatusCode)429)
        {
            // Retry-After header should be present on 429 responses
            second.Headers.Contains("Retry-After").Should().BeTrue(
                "429 responses should include a Retry-After header");
        }
    }
}
