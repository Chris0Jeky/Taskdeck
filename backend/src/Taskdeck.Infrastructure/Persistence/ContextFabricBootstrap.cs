using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Taskdeck.Application.Services;

namespace Taskdeck.Infrastructure.Persistence;

/// <summary>
/// The startup step that runs the ID-preserving capture backfill immediately after
/// <see cref="SerializedMigrator"/> has applied the schema (ADR-0065; CF-01 <c>#2255</c>).
/// <para>
/// It sits here, next to the migrator, because every host that applies migrations shares one SQLite
/// file and therefore owes the same data step: the web API, the standalone MCP stdio and HTTP hosts,
/// and the CLI. Each call is idempotent - on a database whose backfill has finished it costs one
/// marker read and one indexed count.
/// </para>
/// <para>
/// <b>It never blocks startup.</b> A failure leaves the completion marker unset, which keeps Inbox
/// reads on the legacy queue row: the fallback is the shipped behaviour, so refusing to start would
/// trade a degraded read path for no service at all.
/// </para>
/// </summary>
public static class ContextFabricBootstrap
{
    public static void RunCaptureBackfill(IServiceProvider services, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        try
        {
            using var scope = services.CreateScope();
            var backfill = scope.ServiceProvider.GetService<CaptureBackfillService>();
            if (backfill is null)
            {
                return;
            }

            backfill.RunAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            logger?.LogError(
                ex,
                "Context Fabric: the capture backfill did not complete. Inbox reads stay on the legacy " +
                "queue row until it does; no capture is lost.");
        }
    }
}
