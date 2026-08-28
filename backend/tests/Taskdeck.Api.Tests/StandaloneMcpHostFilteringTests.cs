using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Taskdeck.Api.RateLimiting;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class StandaloneMcpHostCollection
{
    public const string Name = "StandaloneMcpHttpHost";
}

/// <summary>
/// End-to-end proof for #1367: boots the REAL standalone MCP HTTP entry point
/// (<c>--mcp --transport http</c>, the only caller of
/// <see cref="Program.ApplyStandaloneMcpHostSecurity"/>) in-process with the hostile
/// separator-only <c>AllowedHosts</c> value and verifies that the rewrite actually reaches
/// HostFilteringMiddleware: a hostile Host header is rejected with 400 while a loopback Host
/// passes host filtering (401 missing-API-key is the pass signal). Deleting the
/// Program.cs guard call, or breaking the post-CreateBuilder config-mutation propagation
/// into HostFilteringOptions, fails this test. Runs in a non-parallelized collection
/// because it mutates process-wide environment variables and drives the assembly entry
/// point, which must not race another test's host bootstrapping.
/// </summary>
[Collection(StandaloneMcpHostCollection.Name)]
public class StandaloneMcpHostFilteringTests
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task StandaloneMcpHttpHost_SeparatorOnlyAllowedHosts_FailsClosedToLoopbackFiltering()
    {
        var port = ReserveFreeLoopbackPort();
        var dbPath = Path.Combine(Path.GetTempPath(), $"taskdeck-mcp-hostfilter-{Guid.NewGuid():N}.db");
        var originalEnvironment = new Dictionary<string, string?>
        {
            ["ConnectionStrings__DefaultConnection"] =
                Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection"),
            ["Connectors__EncryptionKey"] = Environment.GetEnvironmentVariable("Connectors__EncryptionKey"),
            ["AllowedHosts"] = Environment.GetEnvironmentVariable("AllowedHosts")
        };

        var appBuilt = new TaskCompletionSource<WebApplication>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Program.OnStandaloneMcpHttpAppBuilt = app =>
        {
            // Subscribe to ApplicationStarted HERE, inside the seam: it runs synchronously on
            // the entry-point thread between Build() and RunAsync(), so a fast startup cannot
            // fire the token before the test is listening. (Register also invokes the callback
            // immediately if the token were somehow already signaled.)
            app.Lifetime.ApplicationStarted.Register(() => started.TrySetResult());
            appBuilt.TrySetResult(app);
        };

        WebApplication? runningApp = null;
        try
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", TestSqlite.ConnectionString(dbPath));
            Environment.SetEnvironmentVariable(
                "Connectors__EncryptionKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            // The exact #1367 fail-open trigger: separator-only, so IsNullOrWhiteSpace is
            // false while the middleware would parse it to zero hosts and allow every host.
            Environment.SetEnvironmentVariable("AllowedHosts", ";");

            var entryPoint = typeof(Program).Assembly.EntryPoint
                ?? throw new InvalidOperationException("Taskdeck.Api assembly has no entry point.");
            var args = new[] { "--mcp", "--transport", "http", "--port", port.ToString() };
            // For C# async top-level statements the assembly entry point is the compiler's
            // synchronous bridge returning int (verified by execution: this test passes with a
            // plain int result), but handle every shape (int / Task<int> / Task) so the test
            // never depends on that compiler implementation detail.
            var hostTask = Task.Run(async () =>
            {
                var result = entryPoint.Invoke(null, new object[] { args });
                switch (result)
                {
                    case int exit:
                        return exit;
                    case Task<int> asyncExit:
                        return await asyncExit;
                    case Task plainTask:
                        await plainTask;
                        return 0;
                    default:
                        throw new InvalidOperationException(
                            $"Unexpected entry point return: {result?.GetType().ToString() ?? "null"}.");
                }
            });

            // Surface a startup crash directly instead of masking it behind a timeout.
            var completed = await Task.WhenAny(appBuilt.Task, hostTask).WaitAsync(StartupTimeout);
            if (completed == hostTask)
            {
                var earlyExit = await hostTask;
                throw new InvalidOperationException(
                    $"Standalone MCP host exited during startup with code {earlyExit}.");
            }

            var app = await appBuilt.Task;
            runningApp = app;

            // The guard must have rewritten the hostile value before the host was built.
            app.Configuration["AllowedHosts"].Should().Be(Program.StandaloneMcpLoopbackAllowedHosts);

            // The started signal was subscribed inside the seam (before RunAsync), so it
            // cannot be missed even on a fast startup.
            await started.Task.WaitAsync(StartupTimeout);

            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

            // (a) Hostile Host header: HostFilteringMiddleware must reject it with 400.
            using (var hostileRequest = new HttpRequestMessage(HttpMethod.Post, "/mcp"))
            {
                hostileRequest.Headers.Host = "evil.example";
                using var hostileResponse = await client.SendAsync(hostileRequest);
                hostileResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest,
                    "a Host header outside the loopback allowlist must be rejected by host filtering");
            }

            // (b) Loopback Host (127.0.0.1:{port}, set by HttpClient from the base address):
            // must pass host filtering and reach ApiKeyMiddleware, which rejects the missing
            // API key with 401 -- the expected pass signal, and specifically NOT 400.
            using (var loopbackRequest = new HttpRequestMessage(HttpMethod.Post, "/mcp"))
            {
                using var loopbackResponse = await client.SendAsync(loopbackRequest);
                loopbackResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
                    "a loopback Host must pass host filtering and reach API key authentication");
            }

            await app.StopAsync();
            runningApp = null;
            var exitCode = await hostTask.WaitAsync(ShutdownTimeout);
            exitCode.Should().Be(0, "the standalone MCP host must shut down cleanly");
        }
        finally
        {
            Program.OnStandaloneMcpHttpAppBuilt = null;
            if (runningApp is not null)
            {
                // Best-effort teardown on the failure path only; assertions above already
                // report the real failure and a secondary stop error must not mask it.
                try
                {
                    await runningApp.StopAsync();
                }
                catch
                {
                }
            }

            foreach (var (name, value) in originalEnvironment)
            {
                Environment.SetEnvironmentVariable(name, value);
            }

            TryDeleteDatabase(dbPath);
        }
    }

    // End-to-end proof for #1368 in the STANDALONE MCP host: with trusted forwarded headers
    // configured (KnownProxies = the loopback client) the pre-auth FAILURE budget keys on the
    // forwarded client, not the shared proxy socket address. Deleting the Program.cs
    // UseForwardedHeaders wiring makes every client collapse onto 127.0.0.1's single bucket, so the
    // independent-client request (9.9.9.9) would 429 instead of 401 and this test fails.
    //
    // Deterministic despite the failure-budget being cross-request state: a single HttpClient reuses
    // one HTTP/1.1 connection, and Kestrel does not read the next request on a connection until the
    // prior request's full pipeline (including the middleware's post-auth RecordFailedAttempt) has
    // completed — so the first request's consume is always visible to the second.
    [Fact]
    public async Task StandaloneMcpHttpHost_ForwardedHeaders_KeyFailureBudgetOnForwardedClient()
    {
        await RunPreAuthLimiterHostAsync(knownProxy: "127.0.0.1", async client =>
        {
            // Forwarded client A: first unauthenticated attempt fails auth (401) and spends A's budget.
            using (var firstA = await SendForwardedMcpAsync(client, "1.1.1.1"))
            {
                firstA.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
                    "the first attempt reaches API-key auth, which rejects the missing key");
            }

            // Forwarded client A again: its own bucket is now spent, so it is rejected pre-auth (429),
            // proving the budget keys on the forwarded client and is active in the standalone host.
            using (var secondA = await SendForwardedMcpAsync(client, "1.1.1.1"))
            {
                secondA.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
                    "the same forwarded client is limited on its own bucket");
                secondA.Headers.GetValues("X-RateLimit-Policy").Should().ContainSingle()
                    .Which.Should().Be("McpAuthenticationPerIp");
            }

            // Forwarded client B behind the SAME proxy: independent bucket, not starved by A. This is
            // the discriminating assertion — without UseForwardedHeaders both would share 127.0.0.1.
            using (var firstB = await SendForwardedMcpAsync(client, "9.9.9.9"))
            {
                firstB.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
                    "a different forwarded client must have an independent failure budget");
            }
        });
    }

    // Spoof resistance on the REAL standalone wiring: forwarded headers are ENABLED, but the
    // configured trusted proxy is NOT the connecting peer (127.0.0.1). ForwardedHeadersMiddleware
    // must therefore ignore X-Forwarded-For, and the failure budget must key on the socket
    // address — so rotating the XFF value must NOT rotate to a fresh bucket. This exercises the
    // same Program.cs path as the known-proxy test above (not a config-off tautology): the
    // middleware IS in the pipeline and actively refuses the untrusted header.
    [Fact]
    public async Task StandaloneMcpHttpHost_ForwardedHeaders_IgnoreXffFromUnknownPeer()
    {
        await RunPreAuthLimiterHostAsync(knownProxy: "203.0.113.50", async client =>
        {
            // First attempt from the (untrusted) loopback peer spends the SOCKET bucket.
            using (var first = await SendForwardedMcpAsync(client, "1.1.1.1"))
            {
                first.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
                    "the first attempt reaches API-key auth, which rejects the missing key");
            }

            // A rotated X-Forwarded-For from the same untrusted peer must land in the SAME socket
            // bucket (which is now spent): spoofed XFF cannot mint fresh budgets.
            using (var spoofed = await SendForwardedMcpAsync(client, "9.9.9.9"))
            {
                spoofed.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
                    "X-Forwarded-For from a peer outside KnownProxies must be ignored, keying the budget on the socket address");
                spoofed.Headers.GetValues("X-RateLimit-Policy").Should().ContainSingle()
                    .Which.Should().Be("McpAuthenticationPerIp");
            }
        });
    }

    // Parity proof for #1384 in the STANDALONE MCP host (#1372 exact-mirroring discipline): the
    // per-key budget is enforced by ApiKeyMiddleware, which the standalone host wires identically to
    // the co-hosted API. Boots the real --mcp --transport http entry point with McpPerApiKey=1/300s,
    // seeds one valid API key directly into the host's SQLite database, then drives two requests on a
    // single (serialized) connection: the first valid request is admitted (auth passes, one permit
    // spent) and the second — the same key, now over quota — is rejected with the per-key 429 by
    // ApiKeyMiddleware, before the endpoint. The pre-auth IP budget is raised out of the way so only
    // the per-key budget can reject (a valid key never spends the IP failure budget anyway).
    //
    // The first request is a well-formed `initialize` asserted to SUCCEED (#1602). Asserting only
    // NotBe(401)/NotBe(429) let a bare 500 satisfy both, which is exactly how the missing-CORS-
    // middleware defect stayed invisible in this class.
    [Fact]
    public async Task StandaloneMcpHttpHost_PerKeyBudget_RejectsOverQuotaValidKey()
    {
        await RunSeededApiKeyHostAsync(
            "tdsk_standalone_perkey_000000000000000000",
            perKeyPermitLimit: 1,
            async client =>
            {
                using (var first = await PostMcpInitializeAsync(client))
                {
                    var firstBody = await first.Content.ReadAsStringAsync();
                    first.StatusCode.Should().Be(HttpStatusCode.OK,
                        "the first request is inside the per-key budget and must be SERVED by the MCP " +
                        $"endpoint, not merely not-401/not-429; body was: {firstBody}");
                }

                using (var second = await client.PostAsync("/mcp", new StringContent("{}")))
                {
                    second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
                        "the same key is now over its per-key budget and rejected by ApiKeyMiddleware");
                    second.Headers.GetValues("X-RateLimit-Policy").Should().ContainSingle()
                        .Which.Should().Be(RateLimitingPolicyNames.McpPerApiKey);
                    second.Headers.TryGetValues("Retry-After", out _).Should().BeTrue();
                }
            });
    }

    // Regression proof for #1602: the standalone MCP HTTP host must be able to SERVE an MCP request,
    // not only to reject the wrong ones. Every other test in this class stops at a middleware
    // rejection (400/401/429) or a fail-fast exit, so none of them can observe endpoint execution --
    // and the host 500'd on EVERY authenticated request because the mapped MCP endpoint carries CORS
    // metadata (DisableCorsAttribute, McpEndpointMapping.cs) while the standalone pipeline registered
    // no CORS middleware, so EndpointMiddleware threw before the endpoint ever ran.
    //
    // Discriminating by construction: it asserts 200 + a non-empty Mcp-Session-Id + an initialize
    // RESULT in the body. Removing the UseCors/AddCors registration from the standalone branch in
    // Program.cs turns this into a 500 with an empty body (verified in both directions).
    [Fact]
    public async Task StandaloneMcpHttpHost_ValidKey_ServesSuccessfulInitialize()
    {
        await RunSeededApiKeyHostAsync(
            "tdsk_standalone_initialize_00000000000000",
            // Well clear of the budget: this test must be able to fail only on endpoint execution.
            perKeyPermitLimit: 1000,
            async client =>
            {
                using var response = await PostMcpInitializeAsync(client);
                var body = await response.Content.ReadAsStringAsync();

                response.StatusCode.Should().Be(HttpStatusCode.OK,
                    "an authenticated, well-formed initialize must be served by the standalone MCP " +
                    $"host's endpoint (#1602); body was: {body}");
                response.Headers.TryGetValues("Mcp-Session-Id", out var sessionValues).Should().BeTrue(
                    "the standalone host pins Stateless=false, so initialize must mint a session id");
                sessionValues!.Single().Should().NotBeNullOrWhiteSpace();
                body.Should().Contain("\"protocolVersion\"",
                    "the body must carry the MCP initialize RESULT, not an empty 500 body");

                // The CORS middleware added for #1602 must grant NO cross-origin access. Same request
                // with a hostile Origin: it is served (the endpoint runs) but carries no
                // Access-Control-* headers, so a browser cannot read the response. What this assertion
                // actually guards is the DisableCorsAttribute latch on the MCP endpoint --
                // CorsMiddleware sees IDisableCorsAttribute and never evaluates ANY policy, permissive
                // or not (the co-hosted host behaves identically for an ALLOWED origin, see
                // McpHttpTransportApiKeyTests). It would fail if a RequireCors/EnableCors were ever
                // stamped after the latch. The empty policy map is a second, independent latch that
                // this assertion cannot see through the first one.
                using var crossOrigin = await PostMcpInitializeAsync(client, origin: "https://evil.example");

                crossOrigin.StatusCode.Should().Be(HttpStatusCode.OK,
                    "an Origin header must not change server-side handling");
                crossOrigin.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse(
                    "the MCP endpoint carries DisableCorsAttribute, so no origin may ever be allowed");
                crossOrigin.Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse(
                    "no cross-origin credentials may be authorized on the MCP surface");
            });
    }

    /// <summary>
    /// Boots the real standalone MCP HTTP entry point with a per-key budget of
    /// <paramref name="perKeyPermitLimit"/> permits / 300s, seeds <paramref name="plaintextKey"/>
    /// into the host's database, runs <paramref name="assertions"/> against a single-connection
    /// client presenting that key, then shuts the host down cleanly and restores the environment.
    /// </summary>
    private static async Task RunSeededApiKeyHostAsync(
        string plaintextKey,
        int perKeyPermitLimit,
        Func<HttpClient, Task> assertions)
    {
        var port = ReserveFreeLoopbackPort();
        var dbPath = Path.Combine(Path.GetTempPath(), $"taskdeck-mcp-seededkey-{Guid.NewGuid():N}.db");
        var envKeys = new[]
        {
            "ConnectionStrings__DefaultConnection",
            "Connectors__EncryptionKey",
            "AllowedHosts",
            "RateLimiting__Enabled",
            "RateLimiting__McpPerApiKey__PermitLimit",
            "RateLimiting__McpPerApiKey__WindowSeconds",
            "RateLimiting__McpAuthenticationPerIp__PermitLimit",
            "RateLimiting__McpAuthenticationPerIp__WindowSeconds"
        };
        var originalEnvironment = envKeys.ToDictionary(key => key, Environment.GetEnvironmentVariable);

        var appBuilt = new TaskCompletionSource<WebApplication>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Program.OnStandaloneMcpHttpAppBuilt = app =>
        {
            app.Lifetime.ApplicationStarted.Register(() => started.TrySetResult());
            appBuilt.TrySetResult(app);
        };

        WebApplication? runningApp = null;
        try
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", TestSqlite.ConnectionString(dbPath));
            Environment.SetEnvironmentVariable(
                "Connectors__EncryptionKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            Environment.SetEnvironmentVariable("AllowedHosts", null);
            Environment.SetEnvironmentVariable("RateLimiting__Enabled", "true");
            // Caller-chosen per-key budget, long window so it cannot replenish mid-test.
            Environment.SetEnvironmentVariable(
                "RateLimiting__McpPerApiKey__PermitLimit", perKeyPermitLimit.ToString());
            Environment.SetEnvironmentVariable("RateLimiting__McpPerApiKey__WindowSeconds", "300");
            // Raise the pre-auth IP failure budget out of the way — only the per-key budget must reject.
            Environment.SetEnvironmentVariable("RateLimiting__McpAuthenticationPerIp__PermitLimit", "1000");
            Environment.SetEnvironmentVariable("RateLimiting__McpAuthenticationPerIp__WindowSeconds", "60");

            var entryPoint = typeof(Program).Assembly.EntryPoint
                ?? throw new InvalidOperationException("Taskdeck.Api assembly has no entry point.");
            var args = new[] { "--mcp", "--transport", "http", "--port", port.ToString() };
            var hostTask = Task.Run(async () =>
            {
                var result = entryPoint.Invoke(null, new object[] { args });
                return result switch
                {
                    int exit => exit,
                    Task<int> asyncExit => await asyncExit,
                    Task plainTask => await ContinueWithZero(plainTask),
                    _ => throw new InvalidOperationException(
                        $"Unexpected entry point return: {result?.GetType().ToString() ?? "null"}.")
                };
            });

            var completed = await Task.WhenAny(appBuilt.Task, hostTask).WaitAsync(StartupTimeout);
            if (completed == hostTask)
            {
                var earlyExit = await hostTask;
                throw new InvalidOperationException(
                    $"Standalone MCP host exited during startup with code {earlyExit}.");
            }

            var app = await appBuilt.Task;
            runningApp = app;
            await started.Task.WaitAsync(StartupTimeout);

            // Seed a valid key into the host's (already-migrated) database. The host serves only /mcp
            // and no key-management REST surface, so the key is inserted directly. WAL + busy_timeout
            // (same PRAGMAs the host uses) let this second connection write while the host is running.
            await SeedApiKeyAsync(dbPath, plaintextKey);

            // Single client => single reused HTTP/1.1 connection => server-side request serialization.
            using (var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") })
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", plaintextKey);
                await assertions(client);
            }

            await app.StopAsync();
            runningApp = null;
            var exitCode = await hostTask.WaitAsync(ShutdownTimeout);
            exitCode.Should().Be(0, "the standalone MCP host must shut down cleanly");
        }
        finally
        {
            Program.OnStandaloneMcpHttpAppBuilt = null;
            if (runningApp is not null)
            {
                try
                {
                    await runningApp.StopAsync();
                }
                catch
                {
                }
            }

            foreach (var (name, value) in originalEnvironment)
            {
                Environment.SetEnvironmentVariable(name, value);
            }

            TryDeleteDatabase(dbPath);
        }
    }

    private static async Task SeedApiKeyAsync(string dbPath, string plaintextKey)
    {
        var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
            .UseSqlite(TestSqlite.ConnectionString(dbPath))
            .AddInterceptors(new SqlitePragmaConnectionInterceptor(5000))
            .Options;
        await using (var db = new TaskdeckDbContext(options))
        {
            var user = new User("standalone-perkey", "standalone-perkey@example.com", "hash");
            db.Users.Add(user);
            db.ApiKeys.Add(new ApiKey(
                user.Id,
                ApiKeyService.HashKey(plaintextKey),
                plaintextKey[..8],
                "Standalone per-key",
                ApiKeyScope.Full));
            await db.SaveChangesAsync();
        }

        // The seed connection uses Pooling=False (TestSqlite, #1609), so it is fully closed when
        // the DbContext above is disposed and the host is never blocked on this file handle.
    }

    // Fail-fast proof for the standalone host's RateLimiting validation: the co-hosted API runs
    // AddOptionsValidation/ValidateOnStart, but standalone binds RateLimitingSettings manually and
    // constructs the pre-auth limiter eagerly — whose constructor only lower-clamps, so an
    // over-maximum concurrency would otherwise be silently accepted. The explicit validator call in
    // Program.cs must reject it BEFORE the limiter is constructed (the app-built seam never fires)
    // with the clean validation message on stderr and exit code 1.
    [Fact]
    public async Task StandaloneMcpHttpHost_OutOfRangeConcurrency_FailsFastWithValidationMessage()
    {
        var port = ReserveFreeLoopbackPort();
        var dbPath = Path.Combine(Path.GetTempPath(), $"taskdeck-mcp-badconc-{Guid.NewGuid():N}.db");
        var envKeys = new[]
        {
            "ConnectionStrings__DefaultConnection",
            "Connectors__EncryptionKey",
            "AllowedHosts",
            "RateLimiting__Enabled",
            "RateLimiting__McpAuthenticationPerIpConcurrency"
        };
        var originalEnvironment = envKeys.ToDictionary(
            key => key,
            Environment.GetEnvironmentVariable);

        var appBuiltFired = false;
        Program.OnStandaloneMcpHttpAppBuilt = _ => appBuiltFired = true;

        var originalError = Console.Error;
        using var capturedError = new StringWriter();
        try
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", TestSqlite.ConnectionString(dbPath));
            Environment.SetEnvironmentVariable(
                "Connectors__EncryptionKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            Environment.SetEnvironmentVariable("AllowedHosts", null);
            Environment.SetEnvironmentVariable("RateLimiting__Enabled", "true");
            // Above the 10000 maximum: the one direction the limiter constructor cannot clamp.
            Environment.SetEnvironmentVariable("RateLimiting__McpAuthenticationPerIpConcurrency", "10001");

            Console.SetError(capturedError);

            var entryPoint = typeof(Program).Assembly.EntryPoint
                ?? throw new InvalidOperationException("Taskdeck.Api assembly has no entry point.");
            var args = new[] { "--mcp", "--transport", "http", "--port", port.ToString() };
            var hostTask = Task.Run(async () =>
            {
                var result = entryPoint.Invoke(null, new object[] { args });
                return result switch
                {
                    int exit => exit,
                    Task<int> asyncExit => await asyncExit,
                    Task plainTask => await ContinueWithZero(plainTask),
                    _ => throw new InvalidOperationException(
                        $"Unexpected entry point return: {result?.GetType().ToString() ?? "null"}.")
                };
            });

            var exitCode = await hostTask.WaitAsync(StartupTimeout);

            exitCode.Should().Be(1, "an out-of-range concurrency value must fail fast, not start the host");
            appBuiltFired.Should().BeFalse(
                "validation must reject the configuration before the host (and the limiter) is built");
            capturedError.ToString().Should().Contain("McpAuthenticationPerIpConcurrency",
                "the operator must see the clean validation message naming the offending setting");
        }
        finally
        {
            Console.SetError(originalError);
            Program.OnStandaloneMcpHttpAppBuilt = null;
            foreach (var (name, value) in originalEnvironment)
            {
                Environment.SetEnvironmentVariable(name, value);
            }

            TryDeleteDatabase(dbPath);
        }
    }

    /// <summary>
    /// Boots the real standalone MCP HTTP entry point with the pre-auth failure budget set to
    /// 1 permit / 300s and <c>ForwardedHeaders:KnownProxies</c> set to <paramref name="knownProxy"/>,
    /// runs <paramref name="assertions"/> against a single-connection client, then shuts the host
    /// down cleanly and restores the process environment.
    /// </summary>
    private static async Task RunPreAuthLimiterHostAsync(string knownProxy, Func<HttpClient, Task> assertions)
    {
        var port = ReserveFreeLoopbackPort();
        var dbPath = Path.Combine(Path.GetTempPath(), $"taskdeck-mcp-fwd-{Guid.NewGuid():N}.db");
        var envKeys = new[]
        {
            "ConnectionStrings__DefaultConnection",
            "Connectors__EncryptionKey",
            "AllowedHosts",
            "RateLimiting__Enabled",
            "RateLimiting__McpAuthenticationPerIp__PermitLimit",
            "RateLimiting__McpAuthenticationPerIp__WindowSeconds",
            "ForwardedHeaders__KnownProxies__0"
        };
        var originalEnvironment = envKeys.ToDictionary(
            key => key,
            Environment.GetEnvironmentVariable);

        var appBuilt = new TaskCompletionSource<WebApplication>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Program.OnStandaloneMcpHttpAppBuilt = app =>
        {
            app.Lifetime.ApplicationStarted.Register(() => started.TrySetResult());
            appBuilt.TrySetResult(app);
        };

        WebApplication? runningApp = null;
        try
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", TestSqlite.ConnectionString(dbPath));
            Environment.SetEnvironmentVariable(
                "Connectors__EncryptionKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            // Leave AllowedHosts unset so the standalone guard applies its loopback allowlist; the
            // client's Host (127.0.0.1:{port}) passes host filtering.
            Environment.SetEnvironmentVariable("AllowedHosts", null);
            Environment.SetEnvironmentVariable("RateLimiting__Enabled", "true");
            // Failure budget of 1 so a single unauthenticated attempt exhausts a bucket; a long
            // window so it does not replenish mid-test.
            Environment.SetEnvironmentVariable("RateLimiting__McpAuthenticationPerIp__PermitLimit", "1");
            Environment.SetEnvironmentVariable("RateLimiting__McpAuthenticationPerIp__WindowSeconds", "300");
            Environment.SetEnvironmentVariable("ForwardedHeaders__KnownProxies__0", knownProxy);

            var entryPoint = typeof(Program).Assembly.EntryPoint
                ?? throw new InvalidOperationException("Taskdeck.Api assembly has no entry point.");
            var args = new[] { "--mcp", "--transport", "http", "--port", port.ToString() };
            var hostTask = Task.Run(async () =>
            {
                var result = entryPoint.Invoke(null, new object[] { args });
                return result switch
                {
                    int exit => exit,
                    Task<int> asyncExit => await asyncExit,
                    Task plainTask => await ContinueWithZero(plainTask),
                    _ => throw new InvalidOperationException(
                        $"Unexpected entry point return: {result?.GetType().ToString() ?? "null"}.")
                };
            });

            var completed = await Task.WhenAny(appBuilt.Task, hostTask).WaitAsync(StartupTimeout);
            if (completed == hostTask)
            {
                var earlyExit = await hostTask;
                throw new InvalidOperationException(
                    $"Standalone MCP host exited during startup with code {earlyExit}.");
            }

            var app = await appBuilt.Task;
            runningApp = app;
            await started.Task.WaitAsync(StartupTimeout);

            // Single client => single reused HTTP/1.1 connection => server-side request serialization.
            using (var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") })
            {
                await assertions(client);
            }

            await app.StopAsync();
            runningApp = null;
            var exitCode = await hostTask.WaitAsync(ShutdownTimeout);
            exitCode.Should().Be(0, "the standalone MCP host must shut down cleanly");
        }
        finally
        {
            Program.OnStandaloneMcpHttpAppBuilt = null;
            if (runningApp is not null)
            {
                try
                {
                    await runningApp.StopAsync();
                }
                catch
                {
                }
            }

            foreach (var (name, value) in originalEnvironment)
            {
                Environment.SetEnvironmentVariable(name, value);
            }

            TryDeleteDatabase(dbPath);
        }
    }

    private static async Task<int> ContinueWithZero(Task task)
    {
        await task;
        return 0;
    }

    /// <summary>
    /// POSTs a well-formed JSON-RPC <c>initialize</c> to <c>/mcp</c> with the Accept pair the
    /// Streamable HTTP transport requires (<c>application/json</c> + <c>text/event-stream</c>),
    /// mirroring the co-hosted handshake in <c>McpHttpTransportApiKeyTests</c>. The caller supplies
    /// the Authorization header on the client. An <paramref name="origin"/> makes it a cross-origin
    /// request, which is the branch where CORS middleware evaluates a policy.
    /// </summary>
    private static async Task<HttpResponseMessage> PostMcpInitializeAsync(HttpClient client, string? origin = null)
    {
        const string initializeRequest = """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"Taskdeck.Api.Tests","version":"1.0"}}}
            """;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(initializeRequest, Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendForwardedMcpAsync(HttpClient client, string forwardedFor)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwardedFor);
        return await client.SendAsync(request);
    }

    private static int ReserveFreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static void TryDeleteDatabase(string dbPath)
    {
        // Nothing to release first: Pooling=False (TestSqlite, #1609) means no handle outlives
        // its connection, so Windows allows the delete.
        foreach (var path in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Temp files; leaking one on a locked handle is not a test failure.
            }
        }
    }
}
