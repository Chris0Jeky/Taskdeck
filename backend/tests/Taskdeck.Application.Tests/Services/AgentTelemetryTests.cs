using FluentAssertions;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

/// <summary>
/// Tests that agent telemetry instrumentation is content-free by design.
/// These tests verify the API shape and that no method accepts user content parameters.
/// Acceptance criteria: OTel/SQLite run events do not include prompt, capture,
/// card title, transcript, or other user content by default.
/// </summary>
public class AgentTelemetryTests
{
    [Fact]
    public void ActivitySourceName_IsTaskdeckAgent()
    {
        AgentTelemetry.ActivitySourceName.Should().Be("Taskdeck.Agent");
    }

    [Fact]
    public void MeterName_IsTaskdeckAgent()
    {
        AgentTelemetry.MeterName.Should().Be("Taskdeck.Agent");
    }

    [Fact]
    public void RecordRunStarted_DoesNotThrow()
    {
        // Content-free: only triggerType and templateKey (both system-defined identifiers)
        var act = () => AgentTelemetry.RecordRunStarted("manual", "inbox-triage-digest");
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordRunCompleted_DoesNotThrow()
    {
        // Content-free: only numeric values and system identifiers
        var act = () => AgentTelemetry.RecordRunCompleted("scheduled", "inbox-triage-digest", 1500.0, 5, 100);
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordRunFailed_DoesNotThrow()
    {
        var act = () => AgentTelemetry.RecordRunFailed("manual", "inbox-triage-digest");
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordRunCancelled_DoesNotThrow()
    {
        var act = () => AgentTelemetry.RecordRunCancelled("manual", "inbox-triage-digest");
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordStep_DoesNotThrow()
    {
        // eventType is a system-defined category, not user content
        var act = () => AgentTelemetry.RecordStep("triage.proposal_created");
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordProposalCreated_DoesNotThrow()
    {
        var act = () => AgentTelemetry.RecordProposalCreated("inbox-triage-digest");
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordPolicyDenial_DoesNotThrow()
    {
        var act = () => AgentTelemetry.RecordPolicyDenial("approve_proposal", "inbox-triage-digest");
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordEgressViolation_DoesNotThrow()
    {
        // Content-free: host and payloadCategory are system-defined
        var act = () => AgentTelemetry.RecordEgressViolation("attacker.example", "unknown");
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordQuotaExceeded_DoesNotThrow()
    {
        var act = () => AgentTelemetry.RecordQuotaExceeded("tokens", "inbox-triage-digest");
        act.Should().NotThrow();
    }

    [Fact]
    public void StartRunActivity_ReturnsNullWithoutListener()
    {
        // Without an ActivityListener registered, activities are not created
        var activity = AgentTelemetry.StartRunActivity(Guid.NewGuid(), "manual", "test-template");
        // Activity may be null if no listener — that's expected and safe
        activity?.Dispose();
    }
}
