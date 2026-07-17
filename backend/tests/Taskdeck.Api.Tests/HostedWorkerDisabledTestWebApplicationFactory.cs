using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Taskdeck.Api.Tests;

/// <summary>
/// A <see cref="TestWebApplicationFactory"/> whose host registers no background
/// <see cref="IHostedService"/> workers.
///
/// Harness-isolation boundary (issue #1335): the shared web host normally runs
/// <c>LlmQueueToProposalWorker</c> and <c>TranscriptTriageWorker</c>, which poll the
/// LLM queue every <c>Workers:QueuePollIntervalSeconds</c> (1s in tests) and claim exactly
/// the rows a repository-focused test seeds — Pending non-capture rows, Processing capture
/// rows — via the same optimistic-concurrency claim the test itself is asserting on. When a
/// worker's poll lands between seeding and the test's claim, it stamps a fresh
/// <c>UpdatedAt</c>, so the test's expected-timestamp claim(s) all fail and the assertion
/// observes zero successful claims. Removing the hosted services from THIS host makes the
/// claim/read tests deterministic without touching production worker behavior: production and
/// every worker-dependent test class keep the workers via the base factory. Isolation lives in
/// the fixture, not in <c>backend/src</c>.
///
/// Repository integration tests exercise the persistence layer directly through
/// <see cref="WebApplicationFactory{TEntryPoint}.Services"/> scopes and never depend on any
/// background worker, so dropping every application worker here is safe as well as sufficient.
/// </summary>
public sealed class HostedWorkerDisabledTestWebApplicationFactory : TestWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        // ConfigureTestServices runs after the app's own ConfigureServices (including
        // AddTaskdeckWorkers), so this removal is the last word: no application worker is left
        // to start when the host boots, and none can pre-empt the seeded rows.
        //
        // Scope the removal to hosted services implemented in the API assembly (all Taskdeck
        // background workers live there). A blanket RemoveAll<IHostedService>() is deliberately
        // avoided: it would also strip the framework-level GenericWebHostService that starts the
        // TestServer, so any future integration test on this factory that called CreateClient()
        // would hang. Filtering by assembly keeps every framework hosted service intact.
        var apiAssembly = typeof(Program).Assembly;
        builder.ConfigureTestServices(services =>
        {
            var workerDescriptors = services
                .Where(descriptor =>
                {
                    if (descriptor.ServiceType != typeof(IHostedService))
                    {
                        return false;
                    }

                    var implementationType = descriptor.ImplementationType
                        ?? descriptor.ImplementationInstance?.GetType();
                    return implementationType?.Assembly == apiAssembly;
                })
                .ToList();

            foreach (var descriptor in workerDescriptors)
            {
                services.Remove(descriptor);
            }
        });
    }
}
