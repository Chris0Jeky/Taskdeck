using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
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
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", $"Data Source={dbPath}");
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
        // Release pooled SQLite handles so Windows allows the delete.
        SqliteConnection.ClearAllPools();
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
