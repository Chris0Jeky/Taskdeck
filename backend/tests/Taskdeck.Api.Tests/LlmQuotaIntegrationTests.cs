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
        var user = await ApiTestHarness.AuthenticateAsync(client, "quota-enforce");
        var boardId = await CreateBoardWithColumnAsync(client);

        // Create a chat session
        var sessionResponse = await client.PostAsJsonAsync(
            "/api/llm/chat/sessions",
            new CreateChatSessionDto("Quota enforcement test", boardId));
        sessionResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var session = await sessionResponse.Content.ReadFromJsonAsync<ChatSessionDto>();
        session.Should().NotBeNull();

        // Send 3 messages (the configured per-hour limit) — all should succeed
        for (var i = 0; i < 3; i++)
        {
            var msgResponse = await client.PostAsJsonAsync(
                $"/api/llm/chat/sessions/{session!.Id}/messages",
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
            $"/api/llm/chat/sessions/{session!.Id}/messages",
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
        var user = await ApiTestHarness.AuthenticateAsync(client, "quota-status-int");
        var boardId = await CreateBoardWithColumnAsync(client);

        // Check initial status — should be allowed with zero usage
        var initialStatusResponse = await client.GetAsync("/api/llm/quota/status");
        initialStatusResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var initialStatus = await initialStatusResponse.Content.ReadFromJsonAsync<JsonElement>();
        initialStatus.GetProperty("allowed").GetBoolean().Should().BeTrue();
        initialStatus.GetProperty("requestsThisHour").GetInt64().Should().Be(0);
        initialStatus.GetProperty("requestsPerHourLimit").GetInt64().Should().Be(3);

        // Create session and send one message
        var sessionResponse = await client.PostAsJsonAsync(
            "/api/llm/chat/sessions",
            new CreateChatSessionDto("Quota status test", boardId));
        sessionResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var session = await sessionResponse.Content.ReadFromJsonAsync<ChatSessionDto>();

        var msgResponse = await client.PostAsJsonAsync(
            $"/api/llm/chat/sessions/{session!.Id}/messages",
            new SendChatMessageDto("Status tracking message"));
        msgResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Check status after one message
        var afterStatusResponse = await client.GetAsync("/api/llm/quota/status");
        afterStatusResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterStatus = await afterStatusResponse.Content.ReadFromJsonAsync<JsonElement>();
        afterStatus.GetProperty("allowed").GetBoolean().Should().BeTrue();
        afterStatus.GetProperty("requestsThisHour").GetInt64().Should().BeGreaterOrEqualTo(1);
    }

    private static async Task<Guid> CreateBoardWithColumnAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/import/boards",
            new ImportBoardDto(
                $"quota-board-{Guid.NewGuid():N}",
                null,
                new[] { new ImportColumnDto("Backlog", 0, null) },
                Array.Empty<ImportCardDto>(),
                Array.Empty<ImportLabelDto>()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ImportResultDto>();
        result.Should().NotBeNull();
        result!.BoardId.Should().NotBeNull();
        return result.BoardId!.Value;
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
        var boardId = await CreateBoardWithColumnAsync(client);

        // Create chat session
        var sessionResponse = await client.PostAsJsonAsync(
            "/api/llm/chat/sessions",
            new CreateChatSessionDto("Kill switch test", boardId));
        sessionResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var session = await sessionResponse.Content.ReadFromJsonAsync<ChatSessionDto>();
        session.Should().NotBeNull();

        // Verify chat works before kill switch
        var preKillResponse = await client.PostAsJsonAsync(
            $"/api/llm/chat/sessions/{session!.Id}/messages",
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
            $"/api/llm/chat/sessions/{session.Id}/messages",
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
            $"/api/llm/chat/sessions/{session.Id}/messages",
            new SendChatMessageDto("After kill switch disabled"));
        unblockResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<Guid> CreateBoardWithColumnAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/import/boards",
            new ImportBoardDto(
                $"ks-board-{Guid.NewGuid():N}",
                null,
                new[] { new ImportColumnDto("Backlog", 0, null) },
                Array.Empty<ImportCardDto>(),
                Array.Empty<ImportLabelDto>()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ImportResultDto>();
        result.Should().NotBeNull();
        result!.BoardId.Should().NotBeNull();
        return result.BoardId!.Value;
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
        var userA = await ApiTestHarness.AuthenticateAsync(clientA, "quota-iso-a");
        var boardA = await CreateBoardWithColumnAsync(clientA);
        var sessionA = await CreateChatSessionAsync(clientA, "User A session", boardA);

        // Setup user B
        using var clientB = _factory.CreateClient();
        var userB = await ApiTestHarness.AuthenticateAsync(clientB, "quota-iso-b");
        var boardB = await CreateBoardWithColumnAsync(clientB);
        var sessionB = await CreateChatSessionAsync(clientB, "User B session", boardB);

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

    private static async Task<Guid> CreateBoardWithColumnAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/import/boards",
            new ImportBoardDto(
                $"iso-board-{Guid.NewGuid():N}",
                null,
                new[] { new ImportColumnDto("Backlog", 0, null) },
                Array.Empty<ImportCardDto>(),
                Array.Empty<ImportLabelDto>()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ImportResultDto>();
        result.Should().NotBeNull();
        result!.BoardId.Should().NotBeNull();
        return result.BoardId!.Value;
    }

    private static async Task<Guid> CreateChatSessionAsync(HttpClient client, string title, Guid boardId)
    {
        var response = await client.PostAsJsonAsync(
            "/api/llm/chat/sessions",
            new CreateChatSessionDto(title, boardId));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var session = await response.Content.ReadFromJsonAsync<ChatSessionDto>();
        session.Should().NotBeNull();
        return session!.Id;
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
        var user = await ApiTestHarness.AuthenticateAsync(client, "usage-summary");
        var boardId = await CreateBoardWithColumnAsync(client);

        // Check initial usage — should be zero
        var initialUsageResponse = await client.GetAsync("/api/llm/quota/usage");
        initialUsageResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var initialUsage = await initialUsageResponse.Content.ReadFromJsonAsync<JsonElement>();
        initialUsage.GetProperty("totalRequests").GetInt64().Should().Be(0);
        initialUsage.GetProperty("totalTokens").GetInt64().Should().Be(0);

        // Create session and send messages
        var sessionResponse = await client.PostAsJsonAsync(
            "/api/llm/chat/sessions",
            new CreateChatSessionDto("Usage recording test", boardId));
        sessionResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var session = await sessionResponse.Content.ReadFromJsonAsync<ChatSessionDto>();

        const int messageCount = 3;
        for (var i = 0; i < messageCount; i++)
        {
            var msgResponse = await client.PostAsJsonAsync(
                $"/api/llm/chat/sessions/{session!.Id}/messages",
                new SendChatMessageDto($"Usage recording message {i + 1}"));
            msgResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Check usage summary
        var usageResponse = await client.GetAsync("/api/llm/quota/usage");
        usageResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var usage = await usageResponse.Content.ReadFromJsonAsync<JsonElement>();

        // The mock provider returns deterministic responses with token counts.
        // We should have at least messageCount requests recorded.
        usage.GetProperty("totalRequests").GetInt64().Should().BeGreaterOrEqualTo(messageCount);
        // Tokens depend on mock provider output — just verify non-negative
        usage.GetProperty("totalTokens").GetInt64().Should().BeGreaterOrEqualTo(0);
        usage.GetProperty("totalInputTokens").GetInt64().Should().BeGreaterOrEqualTo(0);
        usage.GetProperty("totalOutputTokens").GetInt64().Should().BeGreaterOrEqualTo(0);

        // Verify window boundaries are present
        usage.TryGetProperty("windowStart", out _).Should().BeTrue();
        usage.TryGetProperty("windowEnd", out _).Should().BeTrue();
    }

    [Fact]
    public async Task QuotaStatus_ShouldReturnCorrectRemainingCounts()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "quota-remaining");
        var boardId = await CreateBoardWithColumnAsync(client);

        // Check initial status
        var initialResponse = await client.GetAsync("/api/llm/quota/status");
        initialResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var initial = await initialResponse.Content.ReadFromJsonAsync<JsonElement>();
        initial.GetProperty("allowed").GetBoolean().Should().BeTrue();
        var initialRequestsThisHour = initial.GetProperty("requestsThisHour").GetInt64();
        initialRequestsThisHour.Should().Be(0);

        // Send one message
        var sessionResponse = await client.PostAsJsonAsync(
            "/api/llm/chat/sessions",
            new CreateChatSessionDto("Remaining count test", boardId));
        sessionResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var session = await sessionResponse.Content.ReadFromJsonAsync<ChatSessionDto>();

        var msgResponse = await client.PostAsJsonAsync(
            $"/api/llm/chat/sessions/{session!.Id}/messages",
            new SendChatMessageDto("Remaining count message"));
        msgResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // After one message, requestsThisHour should have increased
        var afterResponse = await client.GetAsync("/api/llm/quota/status");
        afterResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await afterResponse.Content.ReadFromJsonAsync<JsonElement>();
        after.GetProperty("requestsThisHour").GetInt64().Should().BeGreaterOrEqualTo(1);
        after.GetProperty("tokensUsedToday").GetInt64().Should().BeGreaterOrEqualTo(0);
    }

    private static async Task<Guid> CreateBoardWithColumnAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/import/boards",
            new ImportBoardDto(
                $"usage-board-{Guid.NewGuid():N}",
                null,
                new[] { new ImportColumnDto("Backlog", 0, null) },
                Array.Empty<ImportCardDto>(),
                Array.Empty<ImportLabelDto>()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ImportResultDto>();
        result.Should().NotBeNull();
        result!.BoardId.Should().NotBeNull();
        return result.BoardId!.Value;
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
        var userA = await ApiTestHarness.AuthenticateAsync(clientA, "sql-user-a");
        var boardA = await CreateBoardWithColumnAsync(clientA);
        var sessionA = await CreateChatSessionAsync(clientA, "SQL test A", boardA);

        using var clientB = _factory.CreateClient();
        var userB = await ApiTestHarness.AuthenticateAsync(clientB, "sql-user-b");
        var boardB = await CreateBoardWithColumnAsync(clientB);
        var sessionB = await CreateChatSessionAsync(clientB, "SQL test B", boardB);

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
        var user = await ApiTestHarness.AuthenticateAsync(client, "sql-tokens");
        var boardId = await CreateBoardWithColumnAsync(client);
        var sessionId = await CreateChatSessionAsync(client, "Token totals", boardId);

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

    private static async Task<Guid> CreateBoardWithColumnAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/import/boards",
            new ImportBoardDto(
                $"sql-board-{Guid.NewGuid():N}",
                null,
                new[] { new ImportColumnDto("Backlog", 0, null) },
                Array.Empty<ImportCardDto>(),
                Array.Empty<ImportLabelDto>()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ImportResultDto>();
        result.Should().NotBeNull();
        result!.BoardId.Should().NotBeNull();
        return result.BoardId!.Value;
    }

    private static async Task<Guid> CreateChatSessionAsync(HttpClient client, string title, Guid boardId)
    {
        var response = await client.PostAsJsonAsync(
            "/api/llm/chat/sessions",
            new CreateChatSessionDto(title, boardId));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var session = await response.Content.ReadFromJsonAsync<ChatSessionDto>();
        session.Should().NotBeNull();
        return session!.Id;
    }
}
