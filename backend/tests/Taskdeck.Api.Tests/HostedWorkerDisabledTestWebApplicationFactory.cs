using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Taskdeck.Api.Tests;

/// <summary>
/// A <see cref="TestWebApplicationFactory"/> whose host registers no application background
/// workers (<see cref="IHostedService"/> implementations from the API assembly).
///
/// Harness-isolation boundary (issue #1335). Decision rule: use this factory for any
/// repository-focused integration test that seeds state any app worker polls or mutates —
/// LLM queue rows (<c>LlmQueueToProposalWorker</c>/<c>TranscriptTriageWorker</c>), expirable
/// proposals (<c>ProposalHousekeepingWorker</c>), webhook-outbox rows
/// (<c>OutboundWebhookDeliveryWorker</c>), audit logs (<c>AuditRetentionWorker</c>), and
/// embeddings (<c>EmbeddingBackfillWorker</c>). In tests those workers poll every
/// <c>Workers:QueuePollIntervalSeconds</c> (1s) or on their own schedules, and they claim,
/// expire, dead-letter, or delete exactly the rows such tests seed — often via the same
/// optimistic-concurrency stamp the test is asserting on, so a worker landing between seed and
/// assertion makes the test observe zero successes or missing rows. Removing the workers from
/// THIS host makes those tests deterministic without touching production behavior: production
/// and every worker-dependent test class keep the workers via the base factory. Isolation lives
/// in the fixture, not in <c>backend/src</c>.
///
/// Keep worker-dependent classes (e.g. golden-path capture triage flows) on the base factory.
/// </summary>
public sealed class HostedWorkerDisabledTestWebApplicationFactory : TestWebApplicationFactory
{
    /// <summary>
    /// The single contract shared by this factory's removal filter and the regression guard in
    /// <c>TestWebApplicationFactoryTests</c>: a hosted service is an application worker if and
    /// only if its implementation type lives in the API assembly. Every Taskdeck worker is
    /// registered there (see <c>Taskdeck.Api.Extensions.WorkerRegistration</c>); framework
    /// hosted services — notably <c>GenericWebHostService</c>, which starts the TestServer and
    /// MUST survive — live in Microsoft assemblies and never match.
    /// </summary>
    internal static bool IsApplicationWorkerType(Type? implementationType)
        => implementationType?.Assembly == typeof(Program).Assembly;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        // ConfigureTestServices runs after the app's own ConfigureServices (including
        // AddTaskdeckWorkers), so this removal is the last word: no application worker is left
        // to start when the host boots, and none can pre-empt seeded rows.
        //
        // The descriptor filter resolves the implementation type from ImplementationType or
        // ImplementationInstance. A descriptor registered via an ImplementationFactory exposes
        // neither without invoking the factory, so it cannot be classified here — today that
        // gap is empty (all six workers use AddHostedService<T>, which sets ImplementationType),
        // and it is guarded: the regression test resolves the LIVE IHostedService instances and
        // applies the same IsApplicationWorkerType contract, so a factory-registered app worker
        // would fail that test rather than silently reintroducing the flake.
        builder.ConfigureTestServices(services =>
        {
            var workerDescriptors = services
                .Where(descriptor =>
                {
                    if (descriptor.ServiceType != typeof(IHostedService) || descriptor.IsKeyedService)
                    {
                        return false;
                    }

                    var implementationType = descriptor.ImplementationType
                        ?? descriptor.ImplementationInstance?.GetType();
                    return IsApplicationWorkerType(implementationType);
                })
                .ToList();

            foreach (var descriptor in workerDescriptors)
            {
                services.Remove(descriptor);
            }
        });
    }
}
