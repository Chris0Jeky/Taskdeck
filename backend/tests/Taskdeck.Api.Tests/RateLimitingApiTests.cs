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
        using var factory = CreateFactoryWithRateLimits(
            authPermitLimit: 1,
            authWindowSeconds: 1);
        using var client = factory.CreateClient();

        (await SendInvalidLoginAsync(client)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var throttled = await SendInvalidLoginUntilThrottledAsync(client);
        var retryAfterSeconds = await AssertThrottleContractAsync(throttled, RateLimitingPolicyNames.AuthPerIp);
        await Task.Delay(TimeSpan.FromSeconds(retryAfterSeconds) + TimeSpan.FromMilliseconds(250));

        (await SendInvalidLoginAsync(client)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
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
        int captureWindowSeconds = 60)
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

    private static async Task<int> AssertThrottleContractAsync(HttpResponseMessage response, string expectedPolicyName)
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
}
