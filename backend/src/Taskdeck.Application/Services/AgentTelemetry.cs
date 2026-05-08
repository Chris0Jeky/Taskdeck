using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Taskdeck.Application.Services;

/// <summary>
/// Content-free OpenTelemetry instrumentation for agent runtime operations.
/// All recorded values are system-defined identifiers or numeric metrics — never
/// user content (prompts, card titles, transcripts, etc.). Implements GP-10:
/// Explicit Egress And Telemetry Boundaries.
/// </summary>
public static class AgentTelemetry
{
    public const string ActivitySourceName = "Taskdeck.Agent";
    public const string MeterName = "Taskdeck.Agent";

    private static readonly ActivitySource Source = new(ActivitySourceName);
    private static readonly Meter AgentMeter = new(MeterName);

    // Counters — all tags are system-defined identifiers
    private static readonly Counter<long> RunsStarted =
        AgentMeter.CreateCounter<long>("agent.runs.started", "runs", "Number of agent runs started");
    private static readonly Counter<long> RunsCompleted =
        AgentMeter.CreateCounter<long>("agent.runs.completed", "runs", "Number of agent runs completed");
    private static readonly Counter<long> RunsFailed =
        AgentMeter.CreateCounter<long>("agent.runs.failed", "runs", "Number of agent runs failed");
    private static readonly Counter<long> RunsCancelled =
        AgentMeter.CreateCounter<long>("agent.runs.cancelled", "runs", "Number of agent runs cancelled");
    private static readonly Counter<long> StepsExecuted =
        AgentMeter.CreateCounter<long>("agent.steps.executed", "steps", "Number of agent steps executed");
    private static readonly Counter<long> TokensConsumed =
        AgentMeter.CreateCounter<long>("agent.tokens.consumed", "tokens", "Total tokens consumed by agent runs");
    private static readonly Counter<long> ProposalsCreated =
        AgentMeter.CreateCounter<long>("agent.proposals.created", "proposals", "Number of proposals created by agents");
    private static readonly Counter<long> PolicyDenials =
        AgentMeter.CreateCounter<long>("agent.policy.denials", "denials", "Number of policy denials");
    private static readonly Counter<long> EgressViolations =
        AgentMeter.CreateCounter<long>("agent.egress.violations", "violations", "Number of egress violations detected");
    private static readonly Counter<long> QuotaExceeded =
        AgentMeter.CreateCounter<long>("agent.quota.exceeded", "occurrences", "Number of quota exceeded events");

    // Histograms — numeric values only
    private static readonly Histogram<double> RunDuration =
        AgentMeter.CreateHistogram<double>("agent.run.duration_ms", "ms", "Duration of agent runs in milliseconds");
    private static readonly Histogram<int> RunSteps =
        AgentMeter.CreateHistogram<int>("agent.run.steps", "steps", "Number of steps per agent run");

    /// <summary>Records that an agent run started. Tags: triggerType, templateKey.</summary>
    public static void RecordRunStarted(string triggerType, string templateKey)
    {
        RunsStarted.Add(1,
            new KeyValuePair<string, object?>("trigger_type", triggerType),
            new KeyValuePair<string, object?>("template_key", templateKey));
    }

    /// <summary>Records that an agent run completed. Tags: triggerType, templateKey + metrics.</summary>
    public static void RecordRunCompleted(string triggerType, string templateKey, double durationMs, int steps, int tokens)
    {
        RunsCompleted.Add(1,
            new KeyValuePair<string, object?>("trigger_type", triggerType),
            new KeyValuePair<string, object?>("template_key", templateKey));
        RunDuration.Record(durationMs,
            new KeyValuePair<string, object?>("template_key", templateKey));
        RunSteps.Record(steps,
            new KeyValuePair<string, object?>("template_key", templateKey));
        TokensConsumed.Add(tokens,
            new KeyValuePair<string, object?>("template_key", templateKey));
    }

    /// <summary>Records that an agent run failed. Tags: triggerType, templateKey.</summary>
    public static void RecordRunFailed(string triggerType, string templateKey)
    {
        RunsFailed.Add(1,
            new KeyValuePair<string, object?>("trigger_type", triggerType),
            new KeyValuePair<string, object?>("template_key", templateKey));
    }

    /// <summary>Records that an agent run was cancelled. Tags: triggerType, templateKey.</summary>
    public static void RecordRunCancelled(string triggerType, string templateKey)
    {
        RunsCancelled.Add(1,
            new KeyValuePair<string, object?>("trigger_type", triggerType),
            new KeyValuePair<string, object?>("template_key", templateKey));
    }

    /// <summary>Records a single step execution. Tags: eventType (system-defined category).</summary>
    public static void RecordStep(string eventType)
    {
        StepsExecuted.Add(1,
            new KeyValuePair<string, object?>("event_type", eventType));
    }

    /// <summary>Records a proposal creation. Tags: templateKey.</summary>
    public static void RecordProposalCreated(string templateKey)
    {
        ProposalsCreated.Add(1,
            new KeyValuePair<string, object?>("template_key", templateKey));
    }

    /// <summary>Records a policy denial. Tags: toolKey, templateKey.</summary>
    public static void RecordPolicyDenial(string toolKey, string templateKey)
    {
        PolicyDenials.Add(1,
            new KeyValuePair<string, object?>("tool_key", toolKey),
            new KeyValuePair<string, object?>("template_key", templateKey));
    }

    /// <summary>Records an egress violation. Tags: host, payloadCategory (both system-defined).</summary>
    public static void RecordEgressViolation(string host, string payloadCategory)
    {
        EgressViolations.Add(1,
            new KeyValuePair<string, object?>("host", host),
            new KeyValuePair<string, object?>("payload_category", payloadCategory));
    }

    /// <summary>Records a quota exceeded event. Tags: quotaType, templateKey.</summary>
    public static void RecordQuotaExceeded(string quotaType, string templateKey)
    {
        QuotaExceeded.Add(1,
            new KeyValuePair<string, object?>("quota_type", quotaType),
            new KeyValuePair<string, object?>("template_key", templateKey));
    }

    /// <summary>Starts a distributed tracing activity for an agent run. Returns null if no listener.</summary>
    public static Activity? StartRunActivity(Guid runId, string triggerType, string templateKey)
    {
        var activity = Source.StartActivity("agent.run", ActivityKind.Internal);
        activity?.SetTag("agent.run_id", runId.ToString());
        activity?.SetTag("agent.trigger_type", triggerType);
        activity?.SetTag("agent.template_key", templateKey);
        return activity;
    }
}
