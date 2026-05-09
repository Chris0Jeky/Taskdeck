using System.Text.Json;
using Microsoft.Extensions.Logging;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Agents;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

/// <summary>
/// Single entrypoint for agent execution with quotas, tool-bundle allowlists,
/// and inspectable policy decisions.
/// GP-06: No agent bundle may include approve_proposal or direct board mutation tools.
/// GP-09: All runs, policies, and resulting proposals stay inspectable.
/// </summary>
public sealed class AgentRuntime
{
    /// <summary>Maximum number of steps an agent can execute per run.</summary>
    public const int DefaultMaxStepsPerRun = 50;

    /// <summary>Maximum tokens an agent can consume per run.</summary>
    public const int DefaultMaxTokensPerRun = 100_000;

    /// <summary>Maximum concurrent runs per user.</summary>
    public const int DefaultMaxConcurrentRunsPerUser = 3;

    private readonly IUnitOfWork _unitOfWork;
    private readonly AgentPolicy _agentPolicy;
    private readonly IAgentPolicyEvaluator _policyEvaluator;
    private readonly ILogger<AgentRuntime>? _logger;

    public AgentRuntime(
        IUnitOfWork unitOfWork,
        AgentPolicy agentPolicy,
        IAgentPolicyEvaluator policyEvaluator,
        ILogger<AgentRuntime>? logger = null)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _agentPolicy = agentPolicy ?? throw new ArgumentNullException(nameof(agentPolicy));
        _policyEvaluator = policyEvaluator ?? throw new ArgumentNullException(nameof(policyEvaluator));
        _logger = logger;
    }

    /// <summary>
    /// Execute an agent run with full policy enforcement, quota tracking, and event recording.
    /// This is the single entrypoint for all agent execution.
    /// </summary>
    /// <param name="agentProfileId">The agent profile to run.</param>
    /// <param name="userId">The user who owns the profile.</param>
    /// <param name="objective">What the agent is trying to accomplish.</param>
    /// <param name="requestedTools">Tool keys the agent wants to use.</param>
    /// <param name="executeStep">Callback for executing each step — returns (eventType, payload, tokensUsed).</param>
    /// <param name="triggerType">How the run was triggered (manual, scheduled, etc.).</param>
    /// <param name="boardId">Optional board scope.</param>
    /// <param name="maxSteps">Maximum steps allowed (defaults to <see cref="DefaultMaxStepsPerRun"/>).</param>
    /// <param name="maxTokens">Maximum tokens allowed (defaults to <see cref="DefaultMaxTokensPerRun"/>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The completed agent run with all events.</returns>
    public async Task<Result<AgentRunDto>> RunAsync(
        Guid agentProfileId,
        Guid userId,
        string objective,
        IReadOnlyList<string> requestedTools,
        Func<AgentRun, int, CancellationToken, Task<AgentStepResult>> executeStep,
        string triggerType = "manual",
        Guid? boardId = null,
        int? maxSteps = null,
        int? maxTokens = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveMaxSteps = maxSteps ?? DefaultMaxStepsPerRun;
        var effectiveMaxTokens = maxTokens ?? DefaultMaxTokensPerRun;

        // 1. Validate agent profile exists and is owned by the user
        var profile = await _unitOfWork.AgentProfiles.GetByIdAsync(agentProfileId, cancellationToken);
        if (profile is null)
            return Result.Failure<AgentRunDto>(ErrorCodes.NotFound, "Agent profile not found.");

        if (profile.UserId != userId)
            return Result.Failure<AgentRunDto>(ErrorCodes.Forbidden, "Access denied to this agent profile.");

        if (!profile.IsEnabled)
            return Result.Failure<AgentRunDto>(ErrorCodes.InvalidOperation, "Agent profile is disabled.");

        // 2. Check concurrent run quota
        var activeRuns = await _unitOfWork.AgentRuns.GetActiveByUserIdAsync(userId, cancellationToken);
        if (activeRuns.Count() >= DefaultMaxConcurrentRunsPerUser)
        {
            _logger?.LogWarning(
                "User '{UserId}' has reached max concurrent runs ({Max})", userId, DefaultMaxConcurrentRunsPerUser);
            return Result.Failure<AgentRunDto>(ErrorCodes.TooManyRequests,
                $"Maximum of {DefaultMaxConcurrentRunsPerUser} concurrent agent runs allowed.");
        }

        // 3. Validate tool bundle (fail-closed: deny unknown or excluded tools)
        var profileAllowlist = ParseProfileAllowlist(profile.PolicyJson);
        var bundleDecisions = _agentPolicy.ValidateToolBundle(requestedTools, profileAllowlist);
        var denied = bundleDecisions.Where(d => !d.Allowed).ToList();
        if (denied.Count > 0)
        {
            var reasons = string.Join("; ", denied.Select(d => d.Reason));
            _logger?.LogWarning(
                "Agent tool bundle denied for profile '{ProfileId}': {Reasons}",
                agentProfileId, reasons);
            return Result.Failure<AgentRunDto>(ErrorCodes.Forbidden,
                $"Tool bundle validation failed: {reasons}");
        }

        // 4. Create the agent run
        AgentRun run;
        try
        {
            run = new AgentRun(
                agentProfileId,
                userId,
                objective,
                triggerType,
                boardId ?? profile.ScopeBoardId);

            await _unitOfWork.AgentRuns.AddAsync(run, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DomainException ex)
        {
            return Result.Failure<AgentRunDto>(ex.ErrorCode, ex.Message);
        }

        // 5. Record policy decisions as the first event
        var policyEvent = new AgentRunEvent(
            run.Id,
            sequenceNumber: 0,
            eventType: "policy.validated",
            payload: JsonSerializer.Serialize(new
            {
                toolsRequested = requestedTools,
                decisions = bundleDecisions.Select(d => new { d.ToolKey, d.Allowed, d.Reason })
            }));
        await _unitOfWork.AgentRuns.AddEventAsync(policyEvent, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // 6. Execute steps with quota enforcement
        run.TransitionTo(AgentRunStatus.GatheringContext);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var sequenceNumber = 1;
        try
        {
            for (var step = 0; step < effectiveMaxSteps; step++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Check token quota
                if (run.TokensUsed >= effectiveMaxTokens)
                {
                    _logger?.LogWarning(
                        "Agent run '{RunId}' hit token quota ({Tokens}/{Max})",
                        run.Id, run.TokensUsed, effectiveMaxTokens);

                    var quotaEvent = new AgentRunEvent(run.Id, sequenceNumber++,
                        "quota.exceeded", $"{{\"tokensUsed\":{run.TokensUsed},\"maxTokens\":{effectiveMaxTokens}}}");
                    await _unitOfWork.AgentRuns.AddEventAsync(quotaEvent, cancellationToken);

                    run.TransitionTo(AgentRunStatus.Completed,
                        $"Token quota exceeded ({run.TokensUsed}/{effectiveMaxTokens}).");
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    break;
                }

                // Execute the step
                var stepResult = await executeStep(run, step, cancellationToken);

                // Record the step event
                var stepEvent = new AgentRunEvent(run.Id, sequenceNumber++,
                    stepResult.EventType, stepResult.Payload);
                await _unitOfWork.AgentRuns.AddEventAsync(stepEvent, cancellationToken);

                run.IncrementSteps();
                if (stepResult.TokensUsed > 0)
                {
                    run.AddTokenUsage(stepResult.TokensUsed, stepResult.CostUsd);
                }

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                // Check if the step signals completion
                if (stepResult.IsTerminal)
                {
                    if (stepResult.ProposalId.HasValue)
                    {
                        run.AttachProposal(stepResult.ProposalId.Value, stepResult.Summary);
                        run.TransitionTo(AgentRunStatus.ProposalCreated, stepResult.Summary);
                    }
                    else
                    {
                        run.TransitionTo(AgentRunStatus.Completed, stepResult.Summary);
                    }
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    break;
                }
            }

            // If we exhausted all steps without terminal signal
            if (run.Status != AgentRunStatus.Completed && run.Status != AgentRunStatus.ProposalCreated
                && run.Status != AgentRunStatus.Failed)
            {
                var exhaustedEvent = new AgentRunEvent(run.Id, sequenceNumber,
                    "quota.steps_exhausted", $"{{\"stepsExecuted\":{run.StepsExecuted},\"maxSteps\":{effectiveMaxSteps}}}");
                await _unitOfWork.AgentRuns.AddEventAsync(exhaustedEvent, cancellationToken);

                run.TransitionTo(AgentRunStatus.Completed,
                    $"Step quota exhausted ({run.StepsExecuted}/{effectiveMaxSteps}).");
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            run.TransitionTo(AgentRunStatus.Cancelled, "Run was cancelled.");
            try { await _unitOfWork.SaveChangesAsync(CancellationToken.None); }
            catch (Exception saveEx)
            {
                _logger?.LogError(saveEx, "Failed to persist cancellation state for run '{RunId}'", run.Id);
            }
        }
        catch (OperationCanceledException ex)
        {
            _logger?.LogWarning(ex, "Agent run '{RunId}' timed out (not user-cancelled)", run.Id);
            run.MarkFailed("Operation timed out.");
            try { await _unitOfWork.SaveChangesAsync(CancellationToken.None); }
            catch (Exception saveEx)
            {
                _logger?.LogError(saveEx, "Failed to persist timeout state for run '{RunId}'", run.Id);
            }
            return Result.Failure<AgentRunDto>(ErrorCodes.InvalidOperation, "Agent run timed out.");
        }
        catch (EgressViolationException evx)
        {
            _logger?.LogError(evx,
                "Agent run '{RunId}' failed with egress violation: {Violation}",
                run.Id, evx.Violation);

            var egressEvent = new AgentRunEvent(run.Id, sequenceNumber,
                "egress.violation", JsonSerializer.Serialize(new
                {
                    host = evx.Violation.AttemptedHost,
                    uri = evx.Violation.RequestUri,
                    type = evx.Violation.ViolationType.ToString(),
                    reason = evx.Violation.Reason
                }));
            try
            {
                await _unitOfWork.AgentRuns.AddEventAsync(egressEvent, CancellationToken.None);
                run.MarkFailed($"Egress violation: {evx.Violation.Reason}");
                await _unitOfWork.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception saveEx)
            {
                _logger?.LogError(saveEx, "Failed to persist egress violation state for run '{RunId}'", run.Id);
            }
            return Result.Failure<AgentRunDto>(ErrorCodes.Forbidden, $"Egress violation: {evx.Violation.Reason}");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Agent run '{RunId}' failed unexpectedly", run.Id);
            run.MarkFailed($"Unexpected error: {ex.Message}");
            try { await _unitOfWork.SaveChangesAsync(CancellationToken.None); }
            catch (Exception saveEx)
            {
                _logger?.LogError(saveEx, "Failed to persist failure state for run '{RunId}'", run.Id);
            }
            return Result.Failure<AgentRunDto>(ErrorCodes.UnexpectedError, $"Agent run failed: {ex.Message}");
        }

        if (run.Status == AgentRunStatus.Failed)
            return Result.Failure<AgentRunDto>(ErrorCodes.UnexpectedError, run.FailureReason ?? "Agent run failed.");

        return Result.Success(MapToDto(run));
    }

    private static IReadOnlyList<string>? ParseProfileAllowlist(string? policyJson)
    {
        if (string.IsNullOrWhiteSpace(policyJson) || policyJson == "{}")
            return null;

        try
        {
            using var doc = JsonDocument.Parse(policyJson);
            if (doc.RootElement.TryGetProperty("allowedTools", out var toolsElement)
                && toolsElement.ValueKind == JsonValueKind.Array)
            {
                return toolsElement.EnumerateArray()
                    .Select(e => e.GetString())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList()!;
            }
        }
        catch (JsonException)
        {
            // Fail-closed: malformed policy JSON denies all profile-level tools
            return new List<string>();
        }

        // Fail-closed: policy JSON is present but has no allowedTools array —
        // treat as empty allowlist (deny all) rather than null (skip enforcement)
        return new List<string>();
    }

    private static AgentRunDto MapToDto(AgentRun run)
    {
        return new AgentRunDto(
            run.Id,
            run.AgentProfileId,
            run.UserId,
            run.BoardId,
            run.TriggerType,
            run.Objective,
            run.Status,
            run.Summary,
            run.FailureReason,
            run.ProposalId,
            run.StepsExecuted,
            run.TokensUsed,
            run.ApproxCostUsd,
            run.StartedAt,
            run.CompletedAt,
            run.CreatedAt,
            run.UpdatedAt);
    }
}

/// <summary>
/// Result of a single agent execution step.
/// </summary>
public sealed record AgentStepResult
{
    public string EventType { get; }
    public string? Payload { get; }
    public int TokensUsed { get; }
    public decimal? CostUsd { get; }
    public bool IsTerminal { get; }
    public Guid? ProposalId { get; }
    public string? Summary { get; }

    public AgentStepResult(
        string EventType,
        string? Payload = null,
        int TokensUsed = 0,
        decimal? CostUsd = null,
        bool IsTerminal = false,
        Guid? ProposalId = null,
        string? Summary = null)
    {
        if (string.IsNullOrWhiteSpace(EventType))
            throw new ArgumentException("EventType cannot be empty.", nameof(EventType));
        if (TokensUsed < 0)
            throw new ArgumentException("TokensUsed cannot be negative.", nameof(TokensUsed));
        if (ProposalId.HasValue && !IsTerminal)
            throw new ArgumentException("ProposalId can only be set on terminal steps.", nameof(ProposalId));

        this.EventType = EventType;
        this.Payload = Payload;
        this.TokensUsed = TokensUsed;
        this.CostUsd = CostUsd;
        this.IsTerminal = IsTerminal;
        this.ProposalId = ProposalId;
        this.Summary = Summary;
    }
}
