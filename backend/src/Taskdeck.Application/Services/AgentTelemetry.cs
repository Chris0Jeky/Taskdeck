using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Taskdeck.Application.Services;

/// <summary>
/// OpenTelemetry instrumentation for agent runs and events.
/// Content-free by default: captures only counts, durations, status codes,
/// egress host names, and payload categories — never prompt text, capture text,
/// card titles, transcripts, or other user content.
/// GP-10: local telemetry must reject user content by default.
/// </summary>
public static class AgentTelemetry
{
    /// <summary>ActivitySource name for agent operations.</summary>
    public const string ActivitySourceName = "Taskdeck.Agent";

    /// <summary>Meter name for agent metrics.</summary>
    public const string MeterName = "Taskdeck.Agent";

    private static readonly ActivitySource Source = new(ActivitySourceName);
    private static readonly Meter AgentMeter = new(MeterName);

    // Counters (content-free)
    private static readonly Counter<long> RunsStarted = AgentMeter.CreateCounter<long>(
        "agent.runs.started", description: "Number of agent runs started");

    private static readonly Counter<long> RunsCompleted = AgentMeter.CreateCounter<long>(
        "agent.runs.completed", description: "Number of agent runs completed successfully");

    private static readonly Counter<long> RunsFailed = AgentMeter.CreateCounter<long>(
        "agent.runs.failed", description: "Number of agent runs that failed");

    private static readonly Counter<long> RunsCancelled = AgentMeter.CreateCounter<long>(
        "agent.runs.cancelled", description: "Number of agent runs that were cancelled");

    private static readonly Counter<long> StepsExecuted = AgentMeter.CreateCounter<long>(
        "agent.steps.executed", description: "Total agent steps executed");

    private static readonly Counter<long> TokensConsumed = AgentMeter.CreateCounter<long>(
        "agent.tokens.consumed", description: "Total tokens consumed by agent runs");

    private static readonly Counter<long> ProposalsCreated = AgentMeter.CreateCounter<long>(
        "agent.proposals.created", description: "Number of proposals created by agents");

    private static readonly Counter<long> PolicyDenials = AgentMeter.CreateCounter<long>(
        "agent.policy.denials", description: "Number of tool uses denied by policy");

    private static readonly Counter<long> EgressViolations = AgentMeter.CreateCounter<long>(
        "agent.egress.violations", description: "Number of egress violations detected");

    private static readonly Counter<long> QuotaExceeded = AgentMeter.CreateCounter<long>(
        "agent.quota.exceeded", description: "Number of runs stopped by quota limits");

    // Histograms (content-free)
    private static readonly Histogram<double> RunDurationMs = AgentMeter.CreateHistogram<double>(
        "agent.run.duration_ms", description: "Agent run duration in milliseconds");

    private static readonly Histogram<int> StepsPerRun = AgentMeter.CreateHistogram<int>(
        "agent.run.steps", description: "Steps executed per agent run");

    /// <summary>Start an Activity span for an agent run. Returns null if no listener.</summary>
    public static Activity? StartRunActivity(Guid runId, string triggerType, string templateKey)
    {
        var activity = Source.StartActivity("agent.run", ActivityKind.Internal);
        if (activity is not null)
        {
            // Content-free tags only
            activity.SetTag("agent.run.id", runId.ToString());
            activity.SetTag("agent.run.trigger_type", triggerType);
            activity.SetTag("agent.template_key", templateKey);
        }
        return activity;
    }

    /// <summary>Record that a run was started.</summary>
    public static void RecordRunStarted(string triggerType, string templateKey)
    {
        RunsStarted.Add(1,
            new KeyValuePair<string, object?>("trigger_type", triggerType),
            new KeyValuePair<string, object?>("template_key", templateKey));
    }

    /// <summary>Record that a run completed successfully.</summary>
    public static void RecordRunCompleted(string triggerType, string templateKey,
        double durationMs, int steps, int tokens)
    {
        RunsCompleted.Add(1,
            new KeyValuePair<string, object?>("trigger_type", triggerType),
            new KeyValuePair<string, object?>("template_key", templateKey));
        RunDurationMs.Record(durationMs,
            new KeyValuePair<string, object?>("trigger_type", triggerType));
        StepsPerRun.Record(steps,
            new KeyValuePair<string, object?>("trigger_type", triggerType));
        if (tokens > 0)
        {
            TokensConsumed.Add(tokens,
                new KeyValuePair<string, object?>("trigger_type", triggerType));
        }
    }

    /// <summary>Record that a run failed.</summary>
    public static void RecordRunFailed(string triggerType, string templateKey)
    {
        RunsFailed.Add(1,
            new KeyValuePair<string, object?>("trigger_type", triggerType),
            new KeyValuePair<string, object?>("template_key", templateKey));
    }

    /// <summary>Record that a run was cancelled.</summary>
    public static void RecordRunCancelled(string triggerType, string templateKey)
    {
        RunsCancelled.Add(1,
            new KeyValuePair<string, object?>("trigger_type", triggerType),
            new KeyValuePair<string, object?>("template_key", templateKey));
    }

    /// <summary>Record a step execution (content-free).</summary>
    public static void RecordStep(string eventType)
    {
        StepsExecuted.Add(1,
            new KeyValuePair<string, object?>("event_type", eventType));
    }

    /// <summary>Record that a proposal was created by an agent.</summary>
    public static void RecordProposalCreated(string templateKey)
    {
        ProposalsCreated.Add(1,
            new KeyValuePair<string, object?>("template_key", templateKey));
    }

    /// <summary>Record a policy denial.</summary>
    public static void RecordPolicyDenial(string toolKey, string templateKey)
    {
        PolicyDenials.Add(1,
            new KeyValuePair<string, object?>("tool_key", toolKey),
            new KeyValuePair<string, object?>("template_key", templateKey));
    }

    /// <summary>Record an egress violation (content-free: host and category only).</summary>
    public static void RecordEgressViolation(string host, string payloadCategory)
    {
        EgressViolations.Add(1,
            new KeyValuePair<string, object?>("host", host),
            new KeyValuePair<string, object?>("payload_category", payloadCategory));
    }

    /// <summary>Record that a quota limit was hit.</summary>
    public static void RecordQuotaExceeded(string quotaType, string templateKey)
    {
        QuotaExceeded.Add(1,
            new KeyValuePair<string, object?>("quota_type", quotaType),
            new KeyValuePair<string, object?>("template_key", templateKey));
    }
}
