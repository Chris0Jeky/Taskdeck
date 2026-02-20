using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Taskdeck.Api.Telemetry;

public static class TaskdeckTelemetry
{
    public const string ActivitySourceName = "Taskdeck.Api";
    public const string MeterName = "Taskdeck.Api";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName, "1.0.0");

    public static readonly Histogram<long> AutomationQueueBacklog = Meter.CreateHistogram<long>(
        name: "taskdeck.automation.queue.backlog",
        unit: "{items}",
        description: "Pending automation queue depth sampled by workers and health checks.");

    public static readonly Counter<long> WorkerItemsProcessed = Meter.CreateCounter<long>(
        name: "taskdeck.worker.items.processed",
        unit: "{items}",
        description: "Number of worker items processed by outcome.");

    public static readonly Histogram<double> WorkerItemProcessingDurationMs = Meter.CreateHistogram<double>(
        name: "taskdeck.worker.item.processing.duration",
        unit: "ms",
        description: "Worker processing duration per queue item.");

    public static readonly Counter<long> HousekeepingExpiredProposals = Meter.CreateCounter<long>(
        name: "taskdeck.housekeeping.proposals.expired",
        unit: "{items}",
        description: "Number of pending proposals expired by housekeeping worker.");

    public static readonly Histogram<double> WorkerHeartbeatStalenessSeconds = Meter.CreateHistogram<double>(
        name: "taskdeck.worker.heartbeat.staleness",
        unit: "s",
        description: "Observed worker heartbeat staleness.");
}
