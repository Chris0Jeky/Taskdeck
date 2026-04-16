using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Taskdeck.Api.Telemetry;

public static class TaskdeckTelemetry
{
    public const string ActivitySourceName = "Taskdeck.Api";
    public const string MeterName = "Taskdeck.Api";

    public const string McpActivitySourceName = "Taskdeck.Mcp";
    public const string McpMeterName = "Taskdeck.Mcp";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName, "1.0.0");

    public static readonly ActivitySource McpActivitySource = new(McpActivitySourceName);
    public static readonly Meter McpMeter = new(McpMeterName, "1.0.0");

    // ── MCP metrics ─────────────────────────────────────────────────────────
    public static readonly Counter<long> McpRequests = McpMeter.CreateCounter<long>(
        name: "taskdeck.mcp.requests",
        unit: "{requests}",
        description: "Number of MCP requests by operation type and name.");

    public static readonly Histogram<double> McpRequestDurationMs = McpMeter.CreateHistogram<double>(
        name: "taskdeck.mcp.request.duration",
        unit: "ms",
        description: "MCP request processing duration.");

    public static readonly Counter<long> McpErrors = McpMeter.CreateCounter<long>(
        name: "taskdeck.mcp.errors",
        unit: "{errors}",
        description: "Number of MCP errors by operation and error type.");

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
