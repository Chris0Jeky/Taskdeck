using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.IdentityModel.Tokens;
using Taskdeck.Api.Controllers;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Integration tests for OAuth auth code store behavior and JWT token lifecycle.
/// Covers scenarios from #723 (TST-55):
///
/// Section 1: Auth code store — exchange, replay prevention, expiry, concurrent exchange, cleanup
/// Section 2: JWT token lifecycle — expired, wrong key, garbage, deactivated user, password reissue
/// Section 3: SignalR auth — header-based (HubConnection) AND query-string (?access_token=) paths
/// Section 4: Expired JWT across multiple endpoints (systemic 401 + ApiErrorResponse contract)
/// Section 5: GitHub OAuth config endpoints (login redirect, providers)
/// Section 6: Scaling limitation documentation — static ConcurrentDictionary is process-scoped (#676)
/// </summary>
public class OAuthTokenLifecycleTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public OAuthTokenLifecycleTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ─────────────────────────────────────────────────────────
    // 1. Auth code store — ExchangeCode via HTTP pipeline
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ExchangeCode_ValidCode_ReturnsJwtAndConsumesCode()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "oauth-valid");

        var code = InjectAuthCodeForUser(user, TimeSpan.FromSeconds(60));

        // Exchange code via the HTTP endpoint
        using var anonClient = _factory.CreateClient();
        var response = await anonClient.PostAsJsonAsync("/api/auth/github/exchange", new { Code = code });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<AuthResultDto>();
        result.Should().NotBeNull();
        result!.Token.Should().NotBeNullOrWhiteSpace();
        result.User.Should().NotBeNull();
        result.User.Id.Should().Be(user.UserId);

        // Second exchange with same code should fail (consumed)
        var replay = await anonClient.PostAsJsonAsync("/api/auth/github/exchange", new { Code = code });
        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ExchangeCode_ExpiredCode_Returns401WithApiErrorShape()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "oauth-expired");

        // Inject code that expired 10 seconds ago
        var code = InjectAuthCodeForUser(user, TimeSpan.FromSeconds(-10));

        using var anonClient = _factory.CreateClient();
        var response = await anonClient.PostAsJsonAsync("/api/auth/github/exchange", new { Code = code });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ExchangeCode_NonExistentCode_Returns401()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/github/exchange", new { Code = "does-not-exist-abc123" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ExchangeCode_EmptyCode_Returns400()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/github/exchange", new { Code = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ExchangeCode_ReplayPrevention_SecondCallFails()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "oauth-replay");

        var code = InjectAuthCodeForUser(user, TimeSpan.FromSeconds(60));

        using var anonClient = _factory.CreateClient();

        var first = await anonClient.PostAsJsonAsync("/api/auth/github/exchange", new { Code = code });
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await anonClient.PostAsJsonAsync("/api/auth/github/exchange", new { Code = code });
        second.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await ApiTestHarness.AssertErrorContractAsync(second, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ExchangeCode_ConcurrentExchanges_OnlyOneSucceeds()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "oauth-concurrent");

        var code = InjectAuthCodeForUser(user, TimeSpan.FromSeconds(60));

        // Fire multiple concurrent exchange requests for the same code.
        // HttpClient is thread-safe, so reuse a single instance to avoid handler leaks.
        using var exchangeClient = _factory.CreateClient();
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => exchangeClient.PostAsJsonAsync("/api/auth/github/exchange", new { Code = code }))
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var failCount = responses.Count(r => r.StatusCode == HttpStatusCode.Unauthorized);

        successCount.Should().Be(1, "only one concurrent exchange should succeed (single-use code)");
        failCount.Should().Be(4, "the other concurrent exchanges should be rejected");
    }

    [Fact]
    public void AuthCodeStore_CleanupRemovesExpiredCodes()
    {
        // Insert several codes: some expired, some valid
        var expiredCode1 = $"cleanup-expired-1-{Guid.NewGuid():N}";
        var expiredCode2 = $"cleanup-expired-2-{Guid.NewGuid():N}";
        var validCode = $"cleanup-valid-{Guid.NewGuid():N}";

        var dummyResult = CreateDummyAuthResult();

        var dict = GetAuthCodesDict();
        dict[expiredCode1] = (dummyResult, DateTimeOffset.UtcNow.AddSeconds(-30));
        dict[expiredCode2] = (dummyResult, DateTimeOffset.UtcNow.AddSeconds(-5));
        dict[validCode] = (dummyResult, DateTimeOffset.UtcNow.AddSeconds(60));

        // Trigger cleanup by calling CleanupExpiredCodes via reflection
        var cleanupMethod = typeof(AuthController).GetMethod("CleanupExpiredCodes",
            BindingFlags.NonPublic | BindingFlags.Static);
        cleanupMethod!.Invoke(null, null);

        dict.ContainsKey(expiredCode1).Should().BeFalse("expired code should be cleaned up");
        dict.ContainsKey(expiredCode2).Should().BeFalse("expired code should be cleaned up");
        dict.ContainsKey(validCode).Should().BeTrue("valid code should survive cleanup");

        // Clean up the valid code to avoid cross-test interference
        dict.TryRemove(validCode, out _);
    }

    [Fact]
    public void AuthCodeStore_CleanupOnlyTriggeredDuringExchange_ExpiredCodesAccumulate()
    {
        // Document the current behavior: expired codes persist until CleanupExpiredCodes is called.
        // This test validates the behavior described in the issue: cleanup is not on a timer.
        // The static ConcurrentDictionary also does not work with horizontal scaling — see #676.
        var code = $"accumulate-{Guid.NewGuid():N}";
        var dict = GetAuthCodesDict();
        var dummyResult = CreateDummyAuthResult();

        dict[code] = (dummyResult, DateTimeOffset.UtcNow.AddSeconds(-60));

        // Without an exchange attempt, the expired code remains in the dictionary
        dict.ContainsKey(code).Should().BeTrue("expired codes accumulate until cleanup is triggered");

        // Clean up
        dict.TryRemove(code, out _);
    }

    // ─────────────────────────────────────────────────────────
    // 2. JWT token lifecycle — expired, wrong key, invalid
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ExpiredJwt_Returns401WithApiErrorResponseShape()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "tok-expired");

        // Create an already-expired JWT
        var expiredToken = CreateCustomJwt(
            userId: user.UserId,
            username: user.Username,
            email: user.Email,
            secretKey: "TaskdeckDevelopmentOnlySecretKeyChangeMe123!",
            issuer: "Taskdeck",
            audience: "TaskdeckUsers",
            expiresIn: TimeSpan.FromMinutes(-5));

        using var expiredClient = _factory.CreateClient();
        expiredClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", expiredToken);

        var response = await expiredClient.GetAsync("/api/boards");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        // Verify the response is JSON ApiErrorResponse, not HTML
        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WrongSigningKey_Returns401WithApiErrorResponseShape()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "tok-wrongkey");

        // Sign with a completely different secret key
        var wrongKeyToken = CreateCustomJwt(
            userId: user.UserId,
            username: user.Username,
            email: user.Email,
            secretKey: "CompletelyDifferentWrongSecretKey_Padding1234!",
            issuer: "Taskdeck",
            audience: "TaskdeckUsers",
            expiresIn: TimeSpan.FromMinutes(60));

        using var wrongClient = _factory.CreateClient();
        wrongClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", wrongKeyToken);

        var response = await wrongClient.GetAsync("/api/boards");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GarbageToken_Returns401WithApiErrorResponseShape()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "this-is-not-a-jwt");

        var response = await client.GetAsync("/api/boards");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ValidJwt_CanAccessProtectedEndpoint()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "tok-valid");

        var response = await client.GetAsync("/api/boards");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeactivatedUser_Returns401()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "tok-deactivated");

        // Deactivate the user account via the self-scope endpoint.
        // After deactivation, the TokenValidationMiddleware should reject
        // the original token because user.IsActive is false.
        var deactivateResponse = await client.PostAsync($"/api/users/{user.UserId}/deactivate", null);
        deactivateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // The original token is still structurally valid (not expired, correct key),
        // but the middleware rejects it because the user is inactive.
        var response = await client.GetAsync("/api/boards");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ReissuedTokenAfterPasswordChange_CanAccessEndpoint()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "tok-reissue");

        // Change password
        var changePwdResponse = await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            CurrentPassword = "password123",
            NewPassword = "NewSecurePassword!789"
        });
        changePwdResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Login with the new password to get a fresh token
        using var freshClient = _factory.CreateClient();
        var loginResponse = await freshClient.PostAsJsonAsync("/api/auth/login", new
        {
            UsernameOrEmail = user.Username,
            Password = "NewSecurePassword!789"
        });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var freshAuth = await loginResponse.Content.ReadFromJsonAsync<AuthResultDto>();
        freshClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", freshAuth!.Token);

        // Fresh token should work
        var response = await freshClient.GetAsync("/api/boards");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ─────────────────────────────────────────────────────────
    // 3. SignalR auth — header-based (HubConnection via AccessTokenProvider)
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task SignalR_AcceptsValidJwt()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "signalr-qs");

        await using var connection = SignalRTestHelper.CreateBoardsHubConnection(_factory, user.Token);
        await connection.StartAsync();

        connection.State.Should().Be(HubConnectionState.Connected);
    }

    [Fact]
    public async Task SignalR_RejectsExpiredJwt()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "signalr-exp");

        var expiredToken = CreateCustomJwt(
            userId: user.UserId,
            username: user.Username,
            email: user.Email,
            secretKey: "TaskdeckDevelopmentOnlySecretKeyChangeMe123!",
            issuer: "Taskdeck",
            audience: "TaskdeckUsers",
            expiresIn: TimeSpan.FromMinutes(-5));

        await using var connection = SignalRTestHelper.CreateBoardsHubConnection(_factory, expiredToken);

        var act = () => connection.StartAsync();
        await act.Should().ThrowAsync<HttpRequestException>()
            .Where(ex => ex.Message.Contains("401") || ex.StatusCode == HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SignalR_RejectsWrongSigningKey()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "signalr-key");

        var wrongKeyToken = CreateCustomJwt(
            userId: user.UserId,
            username: user.Username,
            email: user.Email,
            secretKey: "CompletelyDifferentWrongSecretKey_Padding1234!",
            issuer: "Taskdeck",
            audience: "TaskdeckUsers",
            expiresIn: TimeSpan.FromMinutes(60));

        await using var connection = SignalRTestHelper.CreateBoardsHubConnection(_factory, wrongKeyToken);

        var act = () => connection.StartAsync();
        await act.Should().ThrowAsync<HttpRequestException>()
            .Where(ex => ex.Message.Contains("401") || ex.StatusCode == HttpStatusCode.Unauthorized);
    }

    // ─────────────────────────────────────────────────────────
    // 3b. SignalR query-string auth — exercises the OnMessageReceived
    //     handler in AuthenticationRegistration.cs that extracts
    //     ?access_token= from the query string for WebSocket connections.
    //     Uses raw HTTP POST to /hubs/boards/negotiate to bypass the
    //     .NET HubConnection client (which always uses the Authorization header).
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task SignalR_QueryStringAuth_ValidTokenAccepted()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "signalr-qsv");

        // POST to the negotiate endpoint with the token in the query string,
        // NOT in the Authorization header. This exercises the OnMessageReceived
        // handler that extracts access_token from Request.Query.
        using var rawClient = _factory.CreateClient();
        var negotiateUrl = $"/hubs/boards/negotiate?access_token={Uri.EscapeDataString(user.Token)}";
        var response = await rawClient.PostAsync(negotiateUrl, null);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "SignalR negotiate should accept a valid JWT passed via ?access_token= query string");
    }

    [Fact]
    public async Task SignalR_QueryStringAuth_ExpiredTokenRejected()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "signalr-qse");

        var expiredToken = CreateCustomJwt(
            userId: user.UserId,
            username: user.Username,
            email: user.Email,
            secretKey: "TaskdeckDevelopmentOnlySecretKeyChangeMe123!",
            issuer: "Taskdeck",
            audience: "TaskdeckUsers",
            expiresIn: TimeSpan.FromMinutes(-5));

        using var rawClient = _factory.CreateClient();
        var negotiateUrl = $"/hubs/boards/negotiate?access_token={Uri.EscapeDataString(expiredToken)}";
        var response = await rawClient.PostAsync(negotiateUrl, null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "SignalR negotiate should reject an expired JWT passed via ?access_token= query string");
    }

    [Fact]
    public async Task SignalR_QueryStringAuth_WrongKeyRejected()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "signalr-qsk");

        var wrongKeyToken = CreateCustomJwt(
            userId: user.UserId,
            username: user.Username,
            email: user.Email,
            secretKey: "CompletelyDifferentWrongSecretKey_Padding1234!",
            issuer: "Taskdeck",
            audience: "TaskdeckUsers",
            expiresIn: TimeSpan.FromMinutes(60));

        using var rawClient = _factory.CreateClient();
        var negotiateUrl = $"/hubs/boards/negotiate?access_token={Uri.EscapeDataString(wrongKeyToken)}";
        var response = await rawClient.PostAsync(negotiateUrl, null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "SignalR negotiate should reject a JWT signed with the wrong key via ?access_token= query string");
    }

    [Fact]
    public async Task SignalR_QueryStringAuth_NoTokenRejected()
    {
        // POST to negotiate without any token at all — should be rejected
        using var rawClient = _factory.CreateClient();
        var response = await rawClient.PostAsync("/hubs/boards/negotiate", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "SignalR negotiate should reject unauthenticated requests");
    }

    // ─────────────────────────────────────────────────────────
    // 4. Expired JWT → multiple endpoints return 401
    //    Verifies that the 401 + ApiErrorResponse contract is
    //    not accidental to one controller, but systemic.
    // ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/api/boards")]
    [InlineData("/api/capture/items")]
    [InlineData("/api/auth/change-password")]
    public async Task ExpiredJwt_MultipleEndpoints_Return401(string endpoint)
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "tok-multi");

        var expiredToken = CreateCustomJwt(
            userId: user.UserId,
            username: user.Username,
            email: user.Email,
            secretKey: "TaskdeckDevelopmentOnlySecretKeyChangeMe123!",
            issuer: "Taskdeck",
            audience: "TaskdeckUsers",
            expiresIn: TimeSpan.FromMinutes(-5));

        using var expiredClient = _factory.CreateClient();
        expiredClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", expiredToken);

        // Use POST for change-password; GET for everything else. Either way should 401 before body validation.
        var response = endpoint.Contains("change-password")
            ? await expiredClient.PostAsJsonAsync(endpoint, new { CurrentPassword = "x", NewPassword = "y" })
            : await expiredClient.GetAsync(endpoint);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            $"endpoint {endpoint} should reject expired JWT");
        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.Unauthorized);
    }

    // ─────────────────────────────────────────────────────────
    // 5. GitHub OAuth config endpoints
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GitHubLogin_WhenNotConfigured_Returns404()
    {
        // The test factory does not configure GitHub OAuth secrets
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/auth/github/login");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Providers_ReturnsGitHubStatus()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/auth/providers");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("gitHub", out var gitHubProp).Should().BeTrue("providers response should contain 'gitHub' property");
        gitHubProp.ValueKind.Should().BeOneOf(new[] { JsonValueKind.True, JsonValueKind.False },
            "gitHub property should be a boolean");
    }

    // ─────────────────────────────────────────────────────────
    // 6. Scaling limitation documentation
    // ─────────────────────────────────────────────────────────
    //
    // IMPORTANT: The OAuth auth code store uses a static ConcurrentDictionary<string, ...>
    // on AuthController. This has the following implications:
    //
    //   1. Codes created on one application instance are invisible to other instances.
    //      In a horizontally-scaled deployment (multiple pods/containers), a code generated
    //      by pod A cannot be exchanged on pod B. This breaks the OAuth flow unless sticky
    //      sessions or a shared store (Redis, database) are used.
    //
    //   2. CleanupExpiredCodes() is only called during ExchangeCode, not on a timer.
    //      Expired codes accumulate indefinitely until an exchange attempt triggers cleanup.
    //      Under low traffic this is a minor memory leak.
    //
    //   3. Codes persist across test runs within the same process since the dictionary
    //      is static. Tests must use unique code values and clean up after themselves.
    //
    // See #676 for the tracking issue to replace this with a distributed store.

    [Fact]
    public void ScalingLimitation_StaticAuthCodeStore_IsProcessScoped()
    {
        // This test documents and verifies the process-scoped nature of the auth code store.
        // A code injected into the static dictionary is visible within the same process but
        // would NOT be visible on a different instance — see #676.
        var code = $"scaling-doc-{Guid.NewGuid():N}";
        var dict = GetAuthCodesDict();
        var dummyResult = CreateDummyAuthResult();

        dict[code] = (dummyResult, DateTimeOffset.UtcNow.AddSeconds(60));

        // The code is visible in the same process (expected for single-instance deployment)
        dict.ContainsKey(code).Should().BeTrue(
            "codes are stored in a static ConcurrentDictionary, visible within the same process");

        // In a multi-instance deployment, this code would NOT be visible on another pod.
        // The ConcurrentDictionary is process-local, not distributed.
        // See #676 for the tracking issue to migrate to a distributed store (Redis/DB).

        // Verify the dictionary is specifically a static field on AuthController
        var field = typeof(AuthController).GetField("_authCodes",
            BindingFlags.NonPublic | BindingFlags.Static);
        field.Should().NotBeNull("auth code store should be a static field on AuthController");
        field!.IsStatic.Should().BeTrue("auth code store must be static (process-scoped)");

        // Clean up to avoid cross-test interference
        dict.TryRemove(code, out _);
    }

    // ─────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────

    private static string InjectAuthCodeForUser(TestUserContext user, TimeSpan validFor)
    {
        var code = $"test-code-{Guid.NewGuid():N}";
        var authResult = new AuthResultDto(user.Token, new UserDto(
            user.UserId, user.Username, user.Email,
            Domain.Enums.UserRole.Editor, true,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        var dict = GetAuthCodesDict();
        dict[code] = (authResult, DateTimeOffset.UtcNow.Add(validFor));
        return code;
    }

    private static ConcurrentDictionary<string, (AuthResultDto Result, DateTimeOffset Expiry)> GetAuthCodesDict()
    {
        var field = typeof(AuthController).GetField("_authCodes",
            BindingFlags.NonPublic | BindingFlags.Static);
        return (ConcurrentDictionary<string, (AuthResultDto Result, DateTimeOffset Expiry)>)field!.GetValue(null)!;
    }

    private static AuthResultDto CreateDummyAuthResult()
    {
        return new AuthResultDto("dummy-token", new UserDto(
            Guid.NewGuid(), "dummy", "dummy@test.com",
            Domain.Enums.UserRole.Editor, true,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
    }

    private static string CreateCustomJwt(
        Guid userId,
        string username,
        string email,
        string secretKey,
        string issuer,
        string audience,
        TimeSpan expiresIn)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var now = DateTime.UtcNow;
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("username", username),
            new Claim("email", email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat,
                new DateTimeOffset(now).ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64)
        };

        // For expired tokens: set notBefore far in the past and expires in the past.
        // For valid tokens: notBefore = now, expires = now + expiresIn.
        var notBefore = expiresIn < TimeSpan.Zero
            ? now.AddMinutes(-60)
            : now;
        var expires = now.Add(expiresIn);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: notBefore,
            expires: expires,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
