using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Custom factory that configures a low quota limit (3 requests/hour) for
/// integration tests that need to exercise quota enforcement end-to-end.
/// Each instance gets its own SQLite database via the base factory.
/// </summary>
public class LowQuotaWebApplicationFactory : TestWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LlmQuota:RequestsPerHour"] = "3",
                ["LlmQuota:TokensPerDay"] = "100000"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Replace the singleton LlmQuotaSettings registered by Program.cs
            // so the low-quota limit takes effect.
            services.RemoveAll<LlmQuotaSettings>();
            services.AddSingleton(new LlmQuotaSettings
            {
                RequestsPerHour = 3,
                TokensPerDay = 100_000
            });
        });
    }
}

// ---------------------------------------------------------------------------
// Test 1: Full-stack quota enforcement
// ---------------------------------------------------------------------------
public class LlmQuotaEnforcementIntegrationTests : IClassFixture<LowQuotaWebApplicationFactory>
{
    private readonly LowQuotaWebApplicationFactory _factory;

    public LlmQuotaEnforcementIntegrationTests(LowQuotaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SendMessage_ShouldReturn429_WhenQuotaExhausted()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "quota-enforce");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(client, "quota");

        // Create a chat session
        var sessionId = await ApiTestHarness.CreateChatSessionAsync(client, "Quota enforcement test", boardId);

        // Send 3 messages (the configured per-hour limit) — all should succeed
        for (var i = 0; i < 3; i++)
        {
            var msgResponse = await client.PostAsJsonAsync(
                $"/api/llm/chat/sessions/{sessionId}/messages",
                new SendChatMessageDto($"Quota test message {i + 1}"));
            var body = await msgResponse.Content.ReadAsStringAsync();
            msgResponse.StatusCode.Should().Be(HttpStatusCode.OK,
                $"message {i + 1} should succeed within quota. Body: {body}");
        }

        // Verify quota status confirms limit reached
        var statusResponse = await client.GetAsync("/api/llm/quota/status");
        var statusBody = await statusResponse.Content.ReadAsStringAsync();

        // The 4th message should be rejected with 429
        var overQuotaResponse = await client.PostAsJsonAsync(
            $"/api/llm/chat/sessions/{sessionId}/messages",
            new SendChatMessageDto("This message exceeds the quota"));

        var overQuotaBody = await overQuotaResponse.Content.ReadAsStringAsync();
        overQuotaResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            $"quota status before: {statusBody}, response: {overQuotaBody}");

        var error = JsonSerializer.Deserialize<JsonElement>(overQuotaBody);
        error.GetProperty("errorCode").GetString().Should().Be("LlmQuotaExceeded");
        error.GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task QuotaStatus_ShouldReflectUsage_AfterMessages()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "quota-status-int");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(client, "quota-status");

        // Check initial status — should be allowed with zero usage
        var initialStatusResponse = await client.GetAsync("/api/llm/quota/status");
        initialStatusResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var initialStatus = await initialStatusResponse.Content.ReadFromJsonAsync<JsonElement>();
        initialStatus.GetProperty("allowed").GetBoolean().Should().BeTrue();
        initialStatus.GetProperty("requestsThisHour").GetInt64().Should().Be(0);
        initialStatus.GetProperty("requestsPerHourLimit").GetInt64().Should().Be(3);

        // Create session and send one message
        var sessionId = await ApiTestHarness.CreateChatSessionAsync(client, "Quota status test", boardId);

        var msgResponse = await client.PostAsJsonAsync(
            $"/api/llm/chat/sessions/{sessionId}/messages",
            new SendChatMessageDto("Status tracking message"));
        msgResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Check status after one message
        var afterStatusResponse = await client.GetAsync("/api/llm/quota/status");
        afterStatusResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterStatus = await afterStatusResponse.Content.ReadFromJsonAsync<JsonElement>();
        afterStatus.GetProperty("allowed").GetBoolean().Should().BeTrue();
        afterStatus.GetProperty("requestsThisHour").GetInt64().Should().Be(1);
    }
}

// ---------------------------------------------------------------------------
// Test 2: Kill-switch blocks and unblocks chat
// ---------------------------------------------------------------------------
public class LlmKillSwitchIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public LlmKillSwitchIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task KillSwitch_ShouldBlockAndUnblockChat()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "ks-block");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(client, "ks");

        // Create chat session
        var sessionId = await ApiTestHarness.CreateChatSessionAsync(client, "Kill switch test", boardId);

        // Verify chat works before kill switch
        var preKillResponse = await client.PostAsJsonAsync(
            $"/api/llm/chat/sessions/{sessionId}/messages",
            new SendChatMessageDto("Before kill switch"));
        preKillResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Enable kill switch for own user
        var enableKsResponse = await client.PostAsJsonAsync("/api/llm/killswitch", new
        {
            scope = (int)KillSwitchScope.Identity,
            target = user.UserId.ToString(),
            enabled = true,
            reason = "integration test block"
        });
        var enableBody = await enableKsResponse.Content.ReadAsStringAsync();
        enableKsResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"enable kill switch failed: {enableBody}");

        // Attempt chat — should be blocked with 503
        var blockedResponse = await client.PostAsJsonAsync(
            $"/api/llm/chat/sessions/{sessionId}/messages",
            new SendChatMessageDto("This should be blocked"));

        blockedResponse.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var blockedError = JsonSerializer.Deserialize<JsonElement>(
            await blockedResponse.Content.ReadAsStringAsync());
        blockedError.GetProperty("errorCode").GetString().Should().Be("LlmKillSwitchActive");

        // Disable kill switch
        var disableKsResponse = await client.PostAsJsonAsync("/api/llm/killswitch", new
        {
            scope = (int)KillSwitchScope.Identity,
            target = user.UserId.ToString(),
            enabled = false,
            reason = (string?)null
        });
        disableKsResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Chat should work again
        var unblockResponse = await client.PostAsJsonAsync(
            $"/api/llm/chat/sessions/{sessionId}/messages",
            new SendChatMessageDto("After kill switch disabled"));
        unblockResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

// ---------------------------------------------------------------------------
// Test 3: Cross-user quota isolation
// ---------------------------------------------------------------------------
public class LlmQuotaCrossUserIsolationTests : IClassFixture<LowQuotaWebApplicationFactory>
{
    private readonly LowQuotaWebApplicationFactory _factory;

    public LlmQuotaCrossUserIsolationTests(LowQuotaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Quota_ShouldBeIsolatedPerUser()
    {
        // Setup user A
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "quota-iso-a");
        var boardA = await ApiTestHarness.CreateBoardWithColumnAsync(clientA, "iso-a");
        var sessionA = await ApiTestHarness.CreateChatSessionAsync(clientA, "User A session", boardA);

        // Setup user B
        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "quota-iso-b");
        var boardB = await ApiTestHarness.CreateBoardWithColumnAsync(clientB, "iso-b");
        var sessionB = await ApiTestHarness.CreateChatSessionAsync(clientB, "User B session", boardB);

        // Exhaust user A's quota (3 messages)
        for (var i = 0; i < 3; i++)
        {
            var resp = await clientA.PostAsJsonAsync(
                $"/api/llm/chat/sessions/{sessionA}/messages",
                new SendChatMessageDto($"User A message {i + 1}"));
            resp.StatusCode.Should().Be(HttpStatusCode.OK,
                $"User A message {i + 1} should succeed");
        }

        // User A is now blocked
        var blockedResponse = await clientA.PostAsJsonAsync(
            $"/api/llm/chat/sessions/{sessionA}/messages",
            new SendChatMessageDto("User A over quota"));
        blockedResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        // User B should still be able to send messages
        var userBResponse = await clientB.PostAsJsonAsync(
            $"/api/llm/chat/sessions/{sessionB}/messages",
            new SendChatMessageDto("User B unaffected"));
        var userBBody = await userBResponse.Content.ReadAsStringAsync();
        userBResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            $"User B should not be blocked by User A's quota. Body: {userBBody}");

        // Verify quota status for each user
        var statusA = await clientA.GetAsync("/api/llm/quota/status");
        var statusAJson = JsonSerializer.Deserialize<JsonElement>(
            await statusA.Content.ReadAsStringAsync());
        statusAJson.GetProperty("allowed").GetBoolean().Should().BeFalse();

        var statusB = await clientB.GetAsync("/api/llm/quota/status");
        var statusBJson = JsonSerializer.Deserialize<JsonElement>(
            await statusB.Content.ReadAsStringAsync());
        statusBJson.GetProperty("allowed").GetBoolean().Should().BeTrue();
    }
}

// ---------------------------------------------------------------------------
// Test 4: Usage recording and summary accuracy
// ---------------------------------------------------------------------------
public class LlmUsageRecordingIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public LlmUsageRecordingIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task UsageSummary_ShouldReflectRecordedUsage()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "usage-summary");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(client, "usage");

        // Check initial usage — should be zero
        var initialUsageResponse = await client.GetAsync("/api/llm/quota/usage");
        initialUsageResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var initialUsage = await initialUsageResponse.Content.ReadFromJsonAsync<JsonElement>();
        initialUsage.GetProperty("totalRequests").GetInt64().Should().Be(0);
        initialUsage.GetProperty("totalTokens").GetInt64().Should().Be(0);

        // Create session and send messages
        var sessionId = await ApiTestHarness.CreateChatSessionAsync(client, "Usage recording test", boardId);

        const int messageCount = 3;
        for (var i = 0; i < messageCount; i++)
        {
            var msgResponse = await client.PostAsJsonAsync(
                $"/api/llm/chat/sessions/{sessionId}/messages",
                new SendChatMessageDto($"Usage recording message {i + 1}"));
            msgResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Check usage summary
        var usageResponse = await client.GetAsync("/api/llm/quota/usage");
        usageResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var usage = await usageResponse.Content.ReadFromJsonAsync<JsonElement>();

        usage.GetProperty("totalRequests").GetInt64().Should().Be(messageCount);
        usage.GetProperty("totalTokens").GetInt64().Should().BeGreaterThanOrEqualTo(0);
        usage.GetProperty("totalInputTokens").GetInt64().Should().BeGreaterThanOrEqualTo(0);
        usage.GetProperty("totalOutputTokens").GetInt64().Should().BeGreaterThanOrEqualTo(0);

        // Verify window boundaries are present
        usage.TryGetProperty("windowStart", out _).Should().BeTrue();
        usage.TryGetProperty("windowEnd", out _).Should().BeTrue();
    }

    [Fact]
    public async Task QuotaStatus_ShouldReturnCorrectRemainingCounts()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "quota-remaining");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(client, "remaining");

        // Check initial status
        var initialResponse = await client.GetAsync("/api/llm/quota/status");
        initialResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var initial = await initialResponse.Content.ReadFromJsonAsync<JsonElement>();
        initial.GetProperty("allowed").GetBoolean().Should().BeTrue();
        initial.GetProperty("requestsThisHour").GetInt64().Should().Be(0);

        // Send one message
        var sessionId = await ApiTestHarness.CreateChatSessionAsync(client, "Remaining count test", boardId);

        var msgResponse = await client.PostAsJsonAsync(
            $"/api/llm/chat/sessions/{sessionId}/messages",
            new SendChatMessageDto("Remaining count message"));
        msgResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // After one message, requestsThisHour should be exactly 1
        var afterResponse = await client.GetAsync("/api/llm/quota/status");
        afterResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await afterResponse.Content.ReadFromJsonAsync<JsonElement>();
        after.GetProperty("requestsThisHour").GetInt64().Should().Be(1);
        after.GetProperty("tokensUsedToday").GetInt64().Should().BeGreaterThanOrEqualTo(0);
    }
}

// ---------------------------------------------------------------------------
// Test 5: SQLite repository correctness
// ---------------------------------------------------------------------------
public class LlmUsageSqliteRepositoryIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public LlmUsageSqliteRepositoryIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SqliteQueries_ShouldFilterByUserCorrectly()
    {
        // This test verifies the raw SQL queries against real SQLite by
        // recording usage for two users and ensuring per-user filtering works.
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "sql-user-a");
        var boardA = await ApiTestHarness.CreateBoardWithColumnAsync(clientA, "sql-a");
        var sessionA = await ApiTestHarness.CreateChatSessionAsync(clientA, "SQL test A", boardA);

        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "sql-user-b");
        var boardB = await ApiTestHarness.CreateBoardWithColumnAsync(clientB, "sql-b");
        var sessionB = await ApiTestHarness.CreateChatSessionAsync(clientB, "SQL test B", boardB);

        // Send 2 messages as user A
        for (var i = 0; i < 2; i++)
        {
            var resp = await clientA.PostAsJsonAsync(
                $"/api/llm/chat/sessions/{sessionA}/messages",
                new SendChatMessageDto($"SQL test A msg {i}"));
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Send 1 message as user B
        var respB = await clientB.PostAsJsonAsync(
            $"/api/llm/chat/sessions/{sessionB}/messages",
            new SendChatMessageDto("SQL test B msg"));
        respB.StatusCode.Should().Be(HttpStatusCode.OK);

        // User A's usage should show 2 requests
        var usageA = await clientA.GetAsync("/api/llm/quota/usage");
        usageA.StatusCode.Should().Be(HttpStatusCode.OK);
        var usageAJson = JsonSerializer.Deserialize<JsonElement>(
            await usageA.Content.ReadAsStringAsync());
        usageAJson.GetProperty("totalRequests").GetInt64().Should().Be(2);

        // User B's usage should show 1 request
        var usageB = await clientB.GetAsync("/api/llm/quota/usage");
        usageB.StatusCode.Should().Be(HttpStatusCode.OK);
        var usageBJson = JsonSerializer.Deserialize<JsonElement>(
            await usageB.Content.ReadAsStringAsync());
        usageBJson.GetProperty("totalRequests").GetInt64().Should().Be(1);
    }

    [Fact]
    public async Task SqliteQueries_ShouldReturnCorrectTokenTotals()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "sql-tokens");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(client, "sql-tokens");
        var sessionId = await ApiTestHarness.CreateChatSessionAsync(client, "Token totals", boardId);

        // Send messages to accumulate tokens
        const int messageCount = 3;
        for (var i = 0; i < messageCount; i++)
        {
            var resp = await client.PostAsJsonAsync(
                $"/api/llm/chat/sessions/{sessionId}/messages",
                new SendChatMessageDto($"Token total msg {i}"));
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Verify usage summary has consistent token math
        var usageResponse = await client.GetAsync("/api/llm/quota/usage");
        usageResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var usage = await usageResponse.Content.ReadFromJsonAsync<JsonElement>();

        var totalInput = usage.GetProperty("totalInputTokens").GetInt64();
        var totalOutput = usage.GetProperty("totalOutputTokens").GetInt64();
        var totalTokens = usage.GetProperty("totalTokens").GetInt64();

        // totalTokens should equal totalInputTokens + totalOutputTokens
        totalTokens.Should().Be(totalInput + totalOutput);
        usage.GetProperty("totalRequests").GetInt64().Should().Be(messageCount);
    }

    [Fact]
    public async Task SqliteQueries_ShouldReturnZero_ForNewUserWithNoUsage()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "sql-empty");

        var usageResponse = await client.GetAsync("/api/llm/quota/usage");
        usageResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var usage = await usageResponse.Content.ReadFromJsonAsync<JsonElement>();

        usage.GetProperty("totalRequests").GetInt64().Should().Be(0);
        usage.GetProperty("totalTokens").GetInt64().Should().Be(0);
        usage.GetProperty("totalInputTokens").GetInt64().Should().Be(0);
        usage.GetProperty("totalOutputTokens").GetInt64().Should().Be(0);
    }
}
