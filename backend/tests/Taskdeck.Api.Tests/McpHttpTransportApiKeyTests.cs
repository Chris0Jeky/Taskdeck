using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Taskdeck.Api.Controllers;
using Taskdeck.Api.RateLimiting;
using Taskdeck.Api.Telemetry;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Integration tests for MCP HTTP transport with API key authentication.
/// Covers: key lifecycle (create/list/revoke), auth middleware behaviour,
/// MCP endpoint access with valid/invalid/expired/revoked keys,
/// cross-user isolation, and rate limiting.
/// </summary>
public class McpHttpTransportApiKeyTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public McpHttpTransportApiKeyTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ── API Key Lifecycle ──────────────────────────────────────────────────────

    [Fact]
    public async Task CreateApiKey_ReturnsPlaintextKey_WithTdskPrefix()
    {
        var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "apikey-create");

        var response = await client.PostAsJsonAsync("/api/apikeys", new CreateApiKeyRequest("Test Key"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await response.Content.ReadFromJsonAsync<CreateApiKeyResponse>();
        result.Should().NotBeNull();
        result!.Key.Should().StartWith("tdsk_");
        result.Key.Length.Should().Be(ApiKey.RawKeyLength);
        result.KeyPrefix.Should().Be(result.Key[..8]);
        result.Name.Should().Be("Test Key");
        result.ExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task CreateApiKey_WithExpiration_SetsExpiresAt()
    {
        var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "apikey-expiry");

        var response = await client.PostAsJsonAsync("/api/apikeys", new CreateApiKeyRequest("Expiring Key", 30));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await response.Content.ReadFromJsonAsync<CreateApiKeyResponse>();
        result!.ExpiresAt.Should().NotBeNull();
        result.ExpiresAt!.Value.Should().BeCloseTo(
            DateTimeOffset.UtcNow.AddDays(30), TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task CreateApiKey_WithEmptyName_Returns400()
    {
        var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "apikey-noname");

        var response = await client.PostAsJsonAsync("/api/apikeys", new CreateApiKeyRequest(""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ListApiKeys_ReturnsCreatedKeys()
    {
        var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "apikey-list");

        // Create two keys
        var r1 = await client.PostAsJsonAsync("/api/apikeys", new CreateApiKeyRequest("Key 1"));
        r1.StatusCode.Should().Be(HttpStatusCode.Created);
        var r2 = await client.PostAsJsonAsync("/api/apikeys", new CreateApiKeyRequest("Key 2"));
        r2.StatusCode.Should().Be(HttpStatusCode.Created);

        var listResponse = await client.GetAsync("/api/apikeys");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await listResponse.Content.ReadFromJsonAsync<ListApiKeysResponse>();
        result.Should().NotBeNull();
        result!.Keys.Count.Should().BeGreaterThanOrEqualTo(2);
        result.Keys.Should().Contain(k => k.Name == "Key 1");
        result.Keys.Should().Contain(k => k.Name == "Key 2");

        // Keys should not expose the full plaintext
        result.Keys.Should().AllSatisfy(k => k.KeyPrefix.Length.Should().Be(8));
    }

    [Fact]
    public async Task RevokeApiKey_MakesKeyInactive()
    {
        var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "apikey-revoke");

        var createResponse = await client.PostAsJsonAsync("/api/apikeys", new CreateApiKeyRequest("Revoke Me"));
        var created = await createResponse.Content.ReadFromJsonAsync<CreateApiKeyResponse>();

        var revokeResponse = await client.DeleteAsync($"/api/apikeys/{created!.Id}");
        revokeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify it shows as revoked in the list
        var listResponse = await client.GetAsync("/api/apikeys");
        var list = await listResponse.Content.ReadFromJsonAsync<ListApiKeysResponse>();
        var revokedKey = list!.Keys.Find(k => k.Id == created.Id);
        revokedKey.Should().NotBeNull();
        revokedKey!.RevokedAt.Should().NotBeNull();
        revokedKey.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task RevokeApiKey_AlreadyRevoked_Returns400()
    {
        var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "apikey-double-revoke");

        var createResponse = await client.PostAsJsonAsync("/api/apikeys", new CreateApiKeyRequest("Double Revoke"));
        var created = await createResponse.Content.ReadFromJsonAsync<CreateApiKeyResponse>();

        await client.DeleteAsync($"/api/apikeys/{created!.Id}");
        var secondRevoke = await client.DeleteAsync($"/api/apikeys/{created.Id}");
        secondRevoke.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RevokeApiKey_WrongUser_Returns403()
    {
        var client1 = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client1, "apikey-owner");

        var createResponse = await client1.PostAsJsonAsync("/api/apikeys", new CreateApiKeyRequest("Owner Key"));
        var created = await createResponse.Content.ReadFromJsonAsync<CreateApiKeyResponse>();

        // Different user tries to revoke
        var client2 = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client2, "apikey-thief");

        var revokeResponse = await client2.DeleteAsync($"/api/apikeys/{created!.Id}");
        revokeResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RevokeApiKey_NonExistent_Returns404()
    {
        var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "apikey-notfound");

        var revokeResponse = await client.DeleteAsync($"/api/apikeys/{Guid.NewGuid()}");
        revokeResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── MCP Endpoint Auth ──────────────────────────────────────────────────────

    [Fact]
    public async Task McpEndpoint_NoAuth_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/mcp", new StringContent("{}"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task McpEndpoint_InvalidKeyFormat_Returns401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-valid-key");

        var response = await client.PostAsync("/mcp", new StringContent("{}"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task McpEndpoint_NonexistentKey_Returns401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", "tdsk_000000000000000000000000000000000000");

        var response = await client.PostAsync("/mcp", new StringContent("{}"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task McpEndpoint_RevokedKey_Returns401()
    {
        var jwtClient = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(jwtClient, "mcp-revoked");

        // Create and revoke a key
        var createResponse = await jwtClient.PostAsJsonAsync("/api/apikeys", new CreateApiKeyRequest("Revoked Key"));
        var created = await createResponse.Content.ReadFromJsonAsync<CreateApiKeyResponse>();
        await jwtClient.DeleteAsync($"/api/apikeys/{created!.Id}");

        // Try to use the revoked key on MCP
        var mcpClient = _factory.CreateClient();
        mcpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Key);

        var response = await mcpClient.PostAsync("/mcp", new StringContent("{}"));
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task McpEndpoint_ValidKey_PassesAuth()
    {
        var jwtClient = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(jwtClient, "mcp-valid");

        var createResponse = await jwtClient.PostAsJsonAsync("/api/apikeys", new CreateApiKeyRequest("Valid MCP Key"));
        var created = await createResponse.Content.ReadFromJsonAsync<CreateApiKeyResponse>();

        // Use the key on MCP endpoint - even though we send a bad body,
        // we should get past auth (not 401)
        var mcpClient = _factory.CreateClient();
        mcpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created!.Key);

        var response = await mcpClient.PostAsync("/mcp", new StringContent("{}"));
        // Should NOT be 401 - the auth layer passed, the MCP layer will handle the bad body
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task McpEndpoint_ValidKey_InitializesAtDocumentedRoute()
    {
        using var jwtClient = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(jwtClient, "mcp-initialize");
        var apiKey = await CreateApiKeyAsync(jwtClient, "Initialize Key");

        using var mcpClient = CreateMcpClient(apiKey);
        using var response = await PostMcpAsync(mcpClient, "/mcp", CreateInitializeRequest(1));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(
            ExtractMcpJson(await response.Content.ReadAsStringAsync()));
        document.RootElement.TryGetProperty("result", out var result).Should().BeTrue();
        result.TryGetProperty("serverInfo", out _).Should().BeTrue();
    }

    [Fact]
    public async Task RootRoute_DoesNotExposeAlternateMcpEndpoint()
    {
        using var jwtClient = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(jwtClient, "mcp-root-route");

        using var response = await PostMcpAsync(jwtClient, "/", CreateInitializeRequest(1));

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        response.Headers.Contains("Mcp-Session-Id").Should().BeFalse();
    }

    [Fact]
    public async Task McpEndpoint_UserScopedResource_IsolatedByApiKeyOwner()
    {
        using var jwtClientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(jwtClientA, "mcp-owner-a");
        var boardA = await ApiTestHarness.CreateBoardAsync(jwtClientA, "mcp-visible-a");
        var apiKeyA = await CreateApiKeyAsync(jwtClientA, "Owner A Key");

        using var jwtClientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(jwtClientB, "mcp-owner-b");
        var boardB = await ApiTestHarness.CreateBoardAsync(jwtClientB, "mcp-visible-b");
        var apiKeyB = await CreateApiKeyAsync(jwtClientB, "Owner B Key");

        using var mcpClientA = CreateMcpClient(apiKeyA);
        using var mcpClientB = CreateMcpClient(apiKeyB);

        var boardsForA = await ReadBoardsResourceAsync(mcpClientA);
        var boardsForB = await ReadBoardsResourceAsync(mcpClientB);

        boardsForA.Should().Contain(boardA.Name).And.NotContain(boardB.Name);
        boardsForB.Should().Contain(boardB.Name).And.NotContain(boardA.Name);
    }

    [Fact]
    public async Task McpEndpoint_RateLimit_IsPartitionedByApiKey()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimiting:Enabled", "true");
            builder.UseSetting("RateLimiting:McpPerApiKey:PermitLimit", "1");
            builder.UseSetting("RateLimiting:McpPerApiKey:WindowSeconds", "60");
        });
        using var jwtClient = factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(jwtClient, "mcp-rate-limit");
        var apiKeyA = await CreateApiKeyAsync(jwtClient, "Rate Key A");
        var apiKeyB = await CreateApiKeyAsync(jwtClient, "Rate Key B");

        using var mcpClientA = factory.CreateClient();
        mcpClientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKeyA);
        using var mcpClientB = factory.CreateClient();
        mcpClientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKeyB);

        using var firstA = await PostMcpAsync(mcpClientA, "/mcp", CreateInitializeRequest(1));
        using var firstB = await PostMcpAsync(mcpClientB, "/mcp", CreateInitializeRequest(1));
        using var secondA = await PostMcpAsync(mcpClientA, "/mcp", CreateInitializeRequest(2));

        firstA.StatusCode.Should().Be(HttpStatusCode.OK);
        firstB.StatusCode.Should().Be(HttpStatusCode.OK,
            "a different key owned by the same user must have an independent budget");
        secondA.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        secondA.Headers.TryGetValues("Retry-After", out _).Should().BeTrue();
        secondA.Headers.GetValues("X-RateLimit-Policy").Should().ContainSingle()
            .Which.Should().Be(RateLimitingPolicyNames.McpPerApiKey);
    }

    [Fact]
    public async Task McpEndpoint_RealRequest_EmitsTelemetry()
    {
        var telemetryObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == TaskdeckTelemetry.McpMeterName
                && instrument.Name == "taskdeck.mcp.requests")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            var isHttpTransport = false;
            var isUnsuccessful = false;
            foreach (var tag in tags)
            {
                isHttpTransport |= tag.Key == TaskdeckTelemetryTags.McpTransport && Equals(tag.Value, "http");
                isUnsuccessful |= tag.Key == TaskdeckTelemetryTags.McpSuccess && Equals(tag.Value, false);
            }

            if (measurement > 0 && isHttpTransport && isUnsuccessful)
            {
                telemetryObserved.TrySetResult(true);
            }
        });
        listener.Start();

        using var client = _factory.CreateClient();
        using var response = await PostMcpAsync(client, "/mcp", CreateInitializeRequest(1));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await telemetryObserved.Task.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("*")]
    public void StandaloneMcpHostSecurity_ReplacesPermissiveAllowedHosts(string? configuredHosts)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedHosts"] = configuredHosts
            })
            .Build();

        Program.ApplyStandaloneMcpHostSecurity(configuration);

        configuration["AllowedHosts"].Should().Be(Program.StandaloneMcpLoopbackAllowedHosts);
    }

    [Fact]
    public void StandaloneMcpHostSecurity_PreservesExplicitAllowedHosts()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "mcp.example.test"
            })
            .Build();

        Program.ApplyStandaloneMcpHostSecurity(configuration);

        configuration["AllowedHosts"].Should().Be("mcp.example.test");
    }

    [Fact]
    public void StandaloneMcpHostSecurity_DefaultBindIsLoopback()
    {
        IPAddress.IsLoopback(IPAddress.Parse(Program.StandaloneMcpDefaultBindHost)).Should().BeTrue();
    }

    // ── Cross-user API Key Isolation ───────────────────────────────────────────

    [Fact]
    public async Task ListApiKeys_OnlyShowsOwnKeys()
    {
        var client1 = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client1, "apikey-iso-a");
        await client1.PostAsJsonAsync("/api/apikeys", new CreateApiKeyRequest("User A Key"));

        var client2 = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client2, "apikey-iso-b");
        await client2.PostAsJsonAsync("/api/apikeys", new CreateApiKeyRequest("User B Key"));

        var listA = await (await client1.GetAsync("/api/apikeys")).Content.ReadFromJsonAsync<ListApiKeysResponse>();
        var listB = await (await client2.GetAsync("/api/apikeys")).Content.ReadFromJsonAsync<ListApiKeysResponse>();

        listA!.Keys.Should().NotContain(k => k.Name == "User B Key");
        listB!.Keys.Should().NotContain(k => k.Name == "User A Key");
    }

    private async Task<string> CreateApiKeyAsync(HttpClient jwtClient, string name)
    {
        using var response = await jwtClient.PostAsJsonAsync("/api/apikeys", new CreateApiKeyRequest(name));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<CreateApiKeyResponse>();
        created.Should().NotBeNull();
        return created!.Key;
    }

    private HttpClient CreateMcpClient(string apiKey)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return client;
    }

    private static async Task<string> ReadBoardsResourceAsync(HttpClient client)
    {
        using var initializeResponse = await PostMcpAsync(client, "/mcp", CreateInitializeRequest(1));
        initializeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        initializeResponse.Headers.TryGetValues("Mcp-Session-Id", out var sessionValues).Should().BeTrue();
        var sessionId = sessionValues!.Single();

        using var initializedResponse = await PostMcpAsync(client, "/mcp", new
        {
            jsonrpc = "2.0",
            method = "notifications/initialized"
        }, sessionId);
        initializedResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        using var resourceResponse = await PostMcpAsync(client, "/mcp", new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "resources/read",
            @params = new { uri = "taskdeck://boards" }
        }, sessionId);
        resourceResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        return await resourceResponse.Content.ReadAsStringAsync();
    }

    private static string ExtractMcpJson(string responseBody)
    {
        if (!responseBody.StartsWith("event:", StringComparison.Ordinal))
        {
            return responseBody;
        }

        var dataLine = responseBody
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Single(line => line.StartsWith("data:", StringComparison.Ordinal));
        return dataLine["data:".Length..].TrimStart();
    }

    private static async Task<HttpResponseMessage> PostMcpAsync(
        HttpClient client,
        string path,
        object payload,
        string? sessionId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (sessionId is not null)
        {
            request.Headers.Add("Mcp-Session-Id", sessionId);
            request.Headers.Add("Mcp-Protocol-Version", "2025-11-25");
        }
        return await client.SendAsync(request);
    }

    private static object CreateInitializeRequest(int id) => new
    {
        jsonrpc = "2.0",
        id,
        method = "initialize",
        @params = new
        {
            protocolVersion = "2025-11-25",
            capabilities = new { },
            clientInfo = new { name = "Taskdeck.Api.Tests", version = "1.0" }
        }
    };

    // ── Key Generation and Hashing ─────────────────────────────────────────────

    [Fact]
    public void GenerateKey_HasCorrectFormat()
    {
        var key = ApiKeyService.GenerateKey();

        key.Should().StartWith("tdsk_");
        key.Length.Should().Be(ApiKey.RawKeyLength);
    }

    [Fact]
    public void GenerateKey_ProducesUniqueKeys()
    {
        var keys = Enumerable.Range(0, 100).Select(_ => ApiKeyService.GenerateKey()).ToList();

        keys.Distinct().Count().Should().Be(100);
    }

    [Fact]
    public void HashKey_ProducesDeterministicHash()
    {
        var key = "tdsk_testkey123456789012345678901234567";
        var hash1 = ApiKeyService.HashKey(key);
        var hash2 = ApiKeyService.HashKey(key);

        hash1.Should().Be(hash2);
        hash1.Length.Should().Be(64); // SHA-256 hex
        hash1.Should().MatchRegex("^[a-f0-9]{64}$"); // lowercase hex
    }

    [Fact]
    public void HashKey_DifferentKeysProduceDifferentHashes()
    {
        var hash1 = ApiKeyService.HashKey("tdsk_key1_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx");
        var hash2 = ApiKeyService.HashKey("tdsk_key2_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx");

        hash1.Should().NotBe(hash2);
    }

    // ── Unauthenticated API key management ─────────────────────────────────────

    [Fact]
    public async Task CreateApiKey_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/apikeys", new CreateApiKeyRequest("No Auth Key"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListApiKeys_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/apikeys");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
