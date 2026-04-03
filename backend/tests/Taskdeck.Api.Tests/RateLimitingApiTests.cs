using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.RateLimiting;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Api.Tests;

public class RateLimitingApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public RateLimitingApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AuthEndpoints_ShouldThrottleAfterBurst_ByClientIp()
    {
        using var factory = CreateFactoryWithRateLimits(
            authPermitLimit: 2,
            authWindowSeconds: 60);
        using var client = factory.CreateClient();

        (await SendInvalidLoginAsync(client)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await SendInvalidLoginAsync(client)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var throttled = await SendInvalidLoginAsync(client);
        await AssertThrottleContractAsync(throttled, RateLimitingPolicyNames.AuthPerIp);
    }

    [Fact]
    public async Task AuthEndpoints_ShouldRecoverAfterWindowReset()
    {
        // Use a 3-second window so CI slowness cannot reset the window between the
        // setup request and the throttle probe — one permit is consumed by the first
        // SendInvalidLoginAsync call and the window must stay active long enough for
        // SendInvalidLoginUntilThrottledAsync to observe the 429.
        using var factory = CreateFactoryWithRateLimits(
            authPermitLimit: 1,
            authWindowSeconds: 3);
        using var client = factory.CreateClient();

        (await SendInvalidLoginAsync(client)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var throttled = await SendInvalidLoginUntilThrottledAsync(client, maxAttempts: 15);
        var retryAfterSeconds = await AssertThrottleContractAsync(throttled, RateLimitingPolicyNames.AuthPerIp);
        using var recovered = await SendInvalidLoginUntilRecoveredAsync(client, retryAfterSeconds);
        recovered.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AuthEndpoints_ShouldIgnoreSpoofedForwardedForHeader()
    {
        using var factory = CreateFactoryWithRateLimits(
            authPermitLimit: 1,
            authWindowSeconds: 60);
        using var client = factory.CreateClient();

        (await SendInvalidLoginAsync(client, forwardedFor: "198.51.100.10")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var throttled = await SendInvalidLoginAsync(client, forwardedFor: "203.0.113.20");
        await AssertThrottleContractAsync(throttled, RateLimitingPolicyNames.AuthPerIp);
    }

    [Fact]
    public async Task AuthEndpoints_ShouldHonorForwardedClientAddress_WhenTrustedProxyConfigured()
    {
        using var factory = CreateFactoryWithRateLimits(
            authPermitLimit: 1,
            authWindowSeconds: 60,
            trustedProxyNetworks: ["127.0.0.0/8", "::1/128"]);
        using var client = factory.CreateClient();

        (await SendInvalidLoginAsync(client, forwardedFor: "198.51.100.10")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await SendInvalidLoginAsync(client, forwardedFor: "203.0.113.20")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var throttled = await SendInvalidLoginAsync(client, forwardedFor: "198.51.100.10");
        await AssertThrottleContractAsync(throttled, RateLimitingPolicyNames.AuthPerIp);
    }

    [Fact]
    public async Task AuthEndpoints_ShouldHonorForwardedClientAddress_WhenTrustedProxyIpConfigured()
    {
        using var factory = CreateFactoryWithRateLimits(
            authPermitLimit: 1,
            authWindowSeconds: 60,
            trustedProxies: ["127.0.0.1", "::1"]);
        using var client = factory.CreateClient();

        (await SendInvalidLoginAsync(client, forwardedFor: "198.51.100.10")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await SendInvalidLoginAsync(client, forwardedFor: "203.0.113.20")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var throttled = await SendInvalidLoginAsync(client, forwardedFor: "198.51.100.10");
        await AssertThrottleContractAsync(throttled, RateLimitingPolicyNames.AuthPerIp);
    }

    [Fact]
    public async Task AuthEndpoints_ShouldAllowConfiguringForwardedHopLimit()
    {
        using var factory = CreateFactoryWithRateLimits(
            authPermitLimit: 1,
            authWindowSeconds: 60,
            trustedProxyNetworks: ["127.0.0.0/8", "::1/128", "10.0.0.0/24"],
            forwardedHeaderLimit: 2);
        using var client = factory.CreateClient();

        (await SendInvalidLoginAsync(client, forwardedFor: "198.51.100.10, 10.0.0.10"))
            .StatusCode
            .Should()
            .Be(HttpStatusCode.Unauthorized);

        var throttled = await SendInvalidLoginAsync(client, forwardedFor: "198.51.100.10, 10.0.0.11");
        await AssertThrottleContractAsync(throttled, RateLimitingPolicyNames.AuthPerIp);
    }

    [Fact]
    public void RateLimitConfiguration_ShouldRejectNonPositiveForwardedHopLimit_WhenTrustConfigured()
    {
        using var factory = CreateFactoryWithRateLimits(
            authPermitLimit: 1,
            authWindowSeconds: 60,
            trustedProxyNetworks: ["127.0.0.0/8"],
            forwardedHeaderLimit: 0);

        Action createClient = () => _ = factory.CreateClient();
        createClient
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*ForwardedHeaders:ForwardLimit*");
    }

    [Fact]
    public async Task AuthRegister_ShouldThrottleAfterBurst_ByClientIp()
    {
        using var factory = CreateFactoryWithRateLimits(
            authPermitLimit: 1,
            authWindowSeconds: 60);
        using var client = factory.CreateClient();

        (await SendRegisterRequestAsync(client)).StatusCode.Should().Be(HttpStatusCode.OK);

        var throttled = await SendRegisterRequestAsync(client);
        await AssertThrottleContractAsync(throttled, RateLimitingPolicyNames.AuthPerIp);
    }

    [Fact]
    public async Task AuthChangePassword_ShouldThrottleAfterBurst_ByClientIp()
    {
        using var factory = CreateFactoryWithRateLimits(
            authPermitLimit: 2,
            authWindowSeconds: 60);
        using var client = factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "rate-password");

        (await SendChangePasswordRequestAsync(client, "password123", "RateLimitPass!456"))
            .StatusCode
            .Should()
            .Be(HttpStatusCode.NoContent);

        var throttled = await SendChangePasswordRequestAsync(client, "RateLimitPass!456", "RateLimitPass!789");
        await AssertThrottleContractAsync(throttled, RateLimitingPolicyNames.AuthPerIp);
    }

    [Fact]
    public async Task LlmQueueMutations_ShouldThrottlePerUser_WithoutCrossUserFalsePositives()
    {
        using var factory = CreateFactoryWithRateLimits(
            authPermitLimit: 200,
            authWindowSeconds: 60,
            hotPathPermitLimit: 1,
            hotPathWindowSeconds: 60);
        using var firstUserClient = factory.CreateClient();
        using var secondUserClient = factory.CreateClient();

        await ApiTestHarness.AuthenticateAsync(firstUserClient, "rate-hotpath-a");
        await ApiTestHarness.AuthenticateAsync(secondUserClient, "rate-hotpath-b");

        var firstEnqueue = await firstUserClient.PostAsJsonAsync(
            "/api/llm-queue",
            new CreateLlmRequestDto("summarize", "hotpath payload A-1"));
        firstEnqueue.StatusCode.Should().Be(HttpStatusCode.OK);

        var secondEnqueueSameUser = await firstUserClient.PostAsJsonAsync(
            "/api/llm-queue",
            new CreateLlmRequestDto("summarize", "hotpath payload A-2"));
        await AssertThrottleContractAsync(secondEnqueueSameUser, RateLimitingPolicyNames.HotPathPerUser);

        var enqueueFromDifferentUser = await secondUserClient.PostAsJsonAsync(
            "/api/llm-queue",
            new CreateLlmRequestDto("summarize", "hotpath payload B-1"));
        enqueueFromDifferentUser.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ChatSessionMutations_ShouldThrottlePerUser_WithoutCrossUserFalsePositives()
    {
        using var factory = CreateFactoryWithRateLimits(
            authPermitLimit: 200,
            authWindowSeconds: 60,
            hotPathPermitLimit: 1,
            hotPathWindowSeconds: 60);
        using var firstUserClient = factory.CreateClient();
        using var secondUserClient = factory.CreateClient();

        await ApiTestHarness.AuthenticateAsync(firstUserClient, "rate-chat-a");
        await ApiTestHarness.AuthenticateAsync(secondUserClient, "rate-chat-b");

        var firstCreate = await firstUserClient.PostAsJsonAsync(
            "/api/llm/chat/sessions",
            new CreateChatSessionDto("chat hotpath A-1"));
        firstCreate.StatusCode.Should().Be(HttpStatusCode.Created);

        var secondCreateSameUser = await firstUserClient.PostAsJsonAsync(
            "/api/llm/chat/sessions",
            new CreateChatSessionDto("chat hotpath A-2"));
        await AssertThrottleContractAsync(secondCreateSameUser, RateLimitingPolicyNames.HotPathPerUser);

        var createFromDifferentUser = await secondUserClient.PostAsJsonAsync(
            "/api/llm/chat/sessions",
            new CreateChatSessionDto("chat hotpath B-1"));
        createFromDifferentUser.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CaptureCreate_ShouldThrottlePerUser_WithoutCrossUserFalsePositives()
    {
        using var factory = CreateFactoryWithRateLimits(
            authPermitLimit: 200,
            authWindowSeconds: 60,
            capturePermitLimit: 1,
            captureWindowSeconds: 60);
        using var firstUserClient = factory.CreateClient();
        using var secondUserClient = factory.CreateClient();

        await ApiTestHarness.AuthenticateAsync(firstUserClient, "rate-capture-a");
        await ApiTestHarness.AuthenticateAsync(secondUserClient, "rate-capture-b");

        var firstCreate = await firstUserClient.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(null, "capture item A-1"));
        firstCreate.StatusCode.Should().Be(HttpStatusCode.Created);

        var secondCreateSameUser = await firstUserClient.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(null, "capture item A-2"));
        await AssertThrottleContractAsync(secondCreateSameUser, RateLimitingPolicyNames.CaptureWritePerUser);

        var createFromDifferentUser = await secondUserClient.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(null, "capture item B-1"));
        createFromDifferentUser.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CaptureTriage_ShouldThrottlePerUser()
    {
        using var factory = CreateFactoryWithRateLimits(
            authPermitLimit: 200,
            authWindowSeconds: 60,
            capturePermitLimit: 2,
            captureWindowSeconds: 60);
        using var client = factory.CreateClient();

        await ApiTestHarness.AuthenticateAsync(client, "rate-triage");

        var createResponse = await client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(null, "triage rate limited item"));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<CaptureItemDto>();
        created.Should().NotBeNull();

        var firstTriage = await client.PostAsync($"/api/capture/items/{created!.Id}/triage", null);
        firstTriage.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var secondTriage = await client.PostAsync($"/api/capture/items/{created.Id}/triage", null);
        await AssertThrottleContractAsync(secondTriage, RateLimitingPolicyNames.CaptureWritePerUser);
    }

    private WebApplicationFactory<Program> CreateFactoryWithRateLimits(
        int authPermitLimit,
        int authWindowSeconds,
        int hotPathPermitLimit = 200,
        int hotPathWindowSeconds = 60,
        int capturePermitLimit = 200,
        int captureWindowSeconds = 60,
        IReadOnlyList<string>? trustedProxyNetworks = null,
        IReadOnlyList<string>? trustedProxies = null,
        int? forwardedHeaderLimit = null)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimiting:Enabled", "true");
            builder.UseSetting("RateLimiting:AuthPerIp:PermitLimit", authPermitLimit.ToString(CultureInfo.InvariantCulture));
            builder.UseSetting("RateLimiting:AuthPerIp:WindowSeconds", authWindowSeconds.ToString(CultureInfo.InvariantCulture));
            builder.UseSetting("RateLimiting:HotPathPerUser:PermitLimit", hotPathPermitLimit.ToString(CultureInfo.InvariantCulture));
            builder.UseSetting("RateLimiting:HotPathPerUser:WindowSeconds", hotPathWindowSeconds.ToString(CultureInfo.InvariantCulture));
            builder.UseSetting("RateLimiting:CaptureWritePerUser:PermitLimit", capturePermitLimit.ToString(CultureInfo.InvariantCulture));
            builder.UseSetting("RateLimiting:CaptureWritePerUser:WindowSeconds", captureWindowSeconds.ToString(CultureInfo.InvariantCulture));
            if (forwardedHeaderLimit is not null)
            {
                builder.UseSetting("ForwardedHeaders:ForwardLimit", forwardedHeaderLimit.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (trustedProxyNetworks is not null)
            {
                for (var i = 0; i < trustedProxyNetworks.Count; i++)
                {
                    builder.UseSetting($"ForwardedHeaders:KnownNetworks:{i}", trustedProxyNetworks[i]);
                }
            }

            if (trustedProxies is null)
            {
                return;
            }

            for (var i = 0; i < trustedProxies.Count; i++)
            {
                builder.UseSetting($"ForwardedHeaders:KnownProxies:{i}", trustedProxies[i]);
            }
        });
    }

    private static async Task<HttpResponseMessage> SendInvalidLoginAsync(HttpClient client, string? forwardedFor = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginDto($"missing-{Guid.NewGuid():N}", "invalid-password"))
        };
        if (!string.IsNullOrWhiteSpace(forwardedFor))
        {
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwardedFor);
        }

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendRegisterRequestAsync(HttpClient client, string? forwardedFor = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
        {
            Content = JsonContent.Create(new CreateUserDto(
                $"rate-user-{suffix}",
                $"rate-{suffix}@example.test",
                "RateLimitPass!123"))
        };
        if (!string.IsNullOrWhiteSpace(forwardedFor))
        {
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwardedFor);
        }

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendChangePasswordRequestAsync(
        HttpClient client,
        string currentPassword,
        string newPassword,
        string? forwardedFor = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new
            {
                CurrentPassword = currentPassword,
                NewPassword = newPassword
            })
        };
        if (!string.IsNullOrWhiteSpace(forwardedFor))
        {
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwardedFor);
        }

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendInvalidLoginUntilThrottledAsync(HttpClient client, int maxAttempts = 5)
    {
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var response = await SendInvalidLoginAsync(client);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return response;
            }

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            response.Dispose();
        }

        throw new Xunit.Sdk.XunitException("Expected auth requests to be throttled within bounded attempts.");
    }

    private static async Task<HttpResponseMessage> SendInvalidLoginUntilRecoveredAsync(HttpClient client, int retryAfterSeconds)
    {
        var maxAttempts = Math.Max(8, retryAfterSeconds * 10);
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var response = await SendInvalidLoginAsync(client);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return response;
            }

            response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
            response.Dispose();
            await Task.Delay(TimeSpan.FromMilliseconds(125));
        }

        throw new Xunit.Sdk.XunitException(
            $"Expected auth requests to recover after rate-limit window reset (retry-after: {retryAfterSeconds}s).");
    }

    private static async Task<int> AssertThrottleContractAsync(HttpResponseMessage response, string expectedPolicyName)
    {
        try
        {
            response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
            response.Headers.TryGetValues("Retry-After", out var retryAfterValues).Should().BeTrue();
            var retryAfterValue = retryAfterValues!.Single();
            int.TryParse(retryAfterValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var retryAfterSeconds)
                .Should()
                .BeTrue();
            retryAfterSeconds.Should().BeGreaterThan(0);

            response.Headers.TryGetValues("X-RateLimit-Policy", out var policyValues).Should().BeTrue();
            policyValues.Should().ContainSingle().Which.Should().Be(expectedPolicyName);

            var payload = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
            payload.Should().NotBeNull();
            payload!.ErrorCode.Should().Be(ErrorCodes.TooManyRequests);
            payload.Message.Should().NotBeNullOrWhiteSpace();
            return retryAfterSeconds;
        }
        finally
        {
            response.Dispose();
        }
    }
}
