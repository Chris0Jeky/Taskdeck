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
/// background worker, so dropping every hosted service here is safe as well as sufficient.
/// </summary>
public sealed class HostedWorkerDisabledTestWebApplicationFactory : TestWebApplicationFactory
{
    /// <summary>
    /// The namespace of every Taskdeck background worker (see
    /// <c>Taskdeck.Api.Extensions.WorkerRegistration</c>). Only hosted services in this
    /// namespace are removed; the framework's own <c>GenericWebHostService</c> — also an
    /// <see cref="IHostedService"/> — MUST stay, or the TestServer would never start.
    /// </summary>
    private const string WorkerNamespace = "Taskdeck.Api.Workers";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        // ConfigureTestServices runs after the app's own ConfigureServices (including
        // AddTaskdeckWorkers), so this removal is the last word: no Taskdeck worker is left to
        // start when the host boots, and none can pre-empt the seeded rows. A blanket
        // RemoveAll<IHostedService>() is deliberately avoided because it would also target the
        // framework web-host service.
        builder.ConfigureTestServices(services =>
        {
            var workerDescriptors = services
                .Where(descriptor =>
                    descriptor.ServiceType == typeof(IHostedService)
                    && (descriptor.ImplementationType?.Namespace?.StartsWith(
                        WorkerNamespace, StringComparison.Ordinal) ?? false))
                .ToList();

            foreach (var descriptor in workerDescriptors)
            {
                services.Remove(descriptor);
            }
        });
    }
}
