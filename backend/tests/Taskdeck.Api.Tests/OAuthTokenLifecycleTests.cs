using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Integration tests for OAuth auth code store behavior and JWT token lifecycle.
/// Covers scenarios from #723 (TST-55): DB-backed auth code store, token issuance,
/// validation, expiration, wrong-key rejection, deactivated user, SignalR query-string auth,
/// and code cleanup semantics.
/// Updated for #676: auth codes now stored in SQLite via OAuthAuthCode entity.
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

        var code = await InjectAuthCodeForUser(user, TimeSpan.FromSeconds(60));

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
        var code = await InjectAuthCodeForUser(user, TimeSpan.FromSeconds(-10));

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

        var code = await InjectAuthCodeForUser(user, TimeSpan.FromSeconds(60));

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

        var code = await InjectAuthCodeForUser(user, TimeSpan.FromSeconds(60));

        // Fire multiple concurrent exchange requests for the same code.
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
    public async Task AuthCodeStore_ExpiredCodesCanBeCleanedUp()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "oauth-cleanup");

        var expiredCode1 = $"cleanup-expired-1-{Guid.NewGuid():N}";
        var expiredCode2 = $"cleanup-expired-2-{Guid.NewGuid():N}";
        var validCode = $"cleanup-valid-{Guid.NewGuid():N}";

        // Insert codes in one scope
        using (var insertScope = _factory.Services.CreateScope())
        {
            await InsertAuthCodeDirectly(insertScope, expiredCode1, user.UserId, user.Token, DateTimeOffset.UtcNow.AddSeconds(-30));
            await InsertAuthCodeDirectly(insertScope, expiredCode2, user.UserId, user.Token, DateTimeOffset.UtcNow.AddSeconds(-5));
            await InsertAuthCodeDirectly(insertScope, validCode, user.UserId, user.Token, DateTimeOffset.UtcNow.AddSeconds(60));
        }

        // Run cleanup in a separate scope (fresh DbContext)
        using (var cleanupScope = _factory.Services.CreateScope())
        {
            var uow = cleanupScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var deleted = await uow.OAuthAuthCodes.DeleteExpiredAsync(DateTimeOffset.UtcNow);
            deleted.Should().BeGreaterThanOrEqualTo(2, "at least the two expired codes should be deleted");
        }

        // Verify in yet another scope
        using (var verifyScope = _factory.Services.CreateScope())
        {
            var uow = verifyScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var remainingCode = await uow.OAuthAuthCodes.GetByCodeAsync(validCode);
            remainingCode.Should().NotBeNull("valid code should survive cleanup");
        }
    }

    // ─────────────────────────────────────────────────────────
    // 2. JWT token lifecycle — expired, wrong key, invalid
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ExpiredJwt_Returns401WithApiErrorResponseShape()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "tok-expired");

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
        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WrongSigningKey_Returns401WithApiErrorResponseShape()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "tok-wrongkey");

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

        var deactivateResponse = await client.PostAsync($"/api/users/{user.UserId}/deactivate", null);
        deactivateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await client.GetAsync("/api/boards");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ReissuedTokenAfterPasswordChange_CanAccessEndpoint()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "tok-reissue");

        var changePwdResponse = await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            CurrentPassword = "password123",
            NewPassword = "NewSecurePassword!789"
        });
        changePwdResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var freshClient = _factory.CreateClient();
        var loginResponse = await freshClient.PostAsJsonAsync("/api/auth/login", new
        {
            UsernameOrEmail = user.Username,
            Password = "NewSecurePassword!789"
        });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var freshAuth = await loginResponse.Content.ReadFromJsonAsync<AuthResultDto>();
        freshClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", freshAuth!.Token);

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
            ? await expiredClient.PostAsJsonAsync(endpoint, new
            {
                CurrentPassword = "password123",
                NewPassword = "NewSecurePassword!789"
            })
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
    // 5. Account linking endpoints
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task LinkedAccounts_ReturnsEmptyListForNewUser()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "link-empty");

        var response = await client.GetAsync("/api/auth/linked-accounts");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var accounts = await response.Content.ReadFromJsonAsync<LinkedAccountDto[]>();
        accounts.Should().NotBeNull();
        accounts!.Should().BeEmpty();
    }

    [Fact]
    public async Task UnlinkGitHub_Returns404_WhenNotLinked()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "unlink-none");

        var response = await client.DeleteAsync("/api/auth/github/link");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task LinkGitHub_Returns401_WhenNotAuthenticated()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/github/link", new { Code = "test" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ─────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────

    private async Task<string> InjectAuthCodeForUser(TestUserContext user, TimeSpan validFor)
    {
        var code = $"test-code-{Guid.NewGuid():N}";
        var expiresAt = DateTimeOffset.UtcNow.Add(validFor);

        using var scope = _factory.Services.CreateScope();
        await InsertAuthCodeDirectly(scope, code, user.UserId, user.Token, expiresAt);

        return code;
    }

    private static async Task InsertAuthCodeDirectly(IServiceScope scope, string code, Guid userId, string token, DateTimeOffset expiresAt)
    {
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();

        // Create a valid auth code first, then adjust ExpiresAt via reflection
        // for testing expired codes (constructor rejects past dates).
        var futureExpiry = DateTimeOffset.UtcNow.AddSeconds(600);
        var authCode = new OAuthAuthCode(code, userId, token, futureExpiry);

        // Override ExpiresAt to the desired value (may be in the past for testing)
        var expiresAtProp = typeof(OAuthAuthCode).GetProperty("ExpiresAt");
        expiresAtProp!.SetValue(authCode, expiresAt);

        db.OAuthAuthCodes.Add(authCode);
        await db.SaveChangesAsync();
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
