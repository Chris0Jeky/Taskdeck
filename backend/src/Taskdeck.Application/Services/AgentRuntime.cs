using System.Diagnostics;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Agents;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

/// <summary>
/// Result of a single step executed by an agent. The step callback returns this
/// to communicate what happened and whether the run should continue.
/// </summary>
public record AgentStepResult(
    string EventType,
    string? Payload = null,
    bool IsTerminal = false,
    Guid? ProposalId = null,
    string? Summary = null,
    int TokensUsed = 0);

/// <summary>
/// Single entrypoint for all agent runs. Enforces quotas, tool-bundle allowlists,
/// concurrent run limits, and records inspectable policy decisions.
/// Implements GP-06 (review-first), GP-09 (traceable expansion), GP-10 (telemetry boundaries).
/// </summary>
public sealed class AgentRuntime
{
    public const int DefaultMaxStepsPerRun = 50;
    public const int DefaultMaxTokensPerRun = 100_000;
    public const int DefaultMaxConcurrentRunsPerUser = 3;

    private readonly IUnitOfWork _unitOfWork;
    private readonly AgentPolicy _agentPolicy;
    private readonly IAgentPolicyEvaluator _policyEvaluator;

    public AgentRuntime(
        IUnitOfWork unitOfWork,
        AgentPolicy agentPolicy,
        IAgentPolicyEvaluator policyEvaluator)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _agentPolicy = agentPolicy ?? throw new ArgumentNullException(nameof(agentPolicy));
        _policyEvaluator = policyEvaluator ?? throw new ArgumentNullException(nameof(policyEvaluator));
    }

    /// <summary>
    /// Runs an agent with the given profile, tools, and step callback.
    /// Validates ownership, tool bundles, and quotas before executing.
    /// </summary>
    public async Task<Result<AgentRunDto>> RunAsync(
        Guid agentProfileId,
        Guid userId,
        string objective,
        IEnumerable<string> requestedTools,
        Func<AgentRun, int, CancellationToken, Task<AgentStepResult>> executeStep,
        string triggerType = "manual",
        Guid? boardId = null,
        int maxSteps = DefaultMaxStepsPerRun,
        int maxTokens = DefaultMaxTokensPerRun,
        CancellationToken cancellationToken = default)
    {
        // 1. Load and validate profile
        var profile = await _unitOfWork.AgentProfiles.GetByIdAsync(agentProfileId, cancellationToken);
        if (profile is null)
            return Result.Failure<AgentRunDto>(ErrorCodes.NotFound, "Agent profile not found.");

        if (profile.UserId != userId)
            return Result.Failure<AgentRunDto>(ErrorCodes.Forbidden, "Agent profile does not belong to the requesting user.");

        if (!profile.IsEnabled)
            return Result.Failure<AgentRunDto>(ErrorCodes.InvalidOperation, "Agent profile is disabled.");

        // 2. Validate tool bundle
        var toolList = requestedTools.ToList();
        var bundleDecisions = _agentPolicy.ValidateToolBundle(toolList);
        var deniedTools = bundleDecisions.Where(d => !d.Allowed).ToList();
        if (deniedTools.Count > 0)
        {
            var firstDenied = deniedTools[0];
            var reason = AgentPolicy.PermanentlyExcludedTools.Contains(firstDenied.ToolKey)
                ? $"Tool '{firstDenied.ToolKey}' is permanently excluded from agent bundles."
                : firstDenied.Reason;
            return Result.Failure<AgentRunDto>(ErrorCodes.Forbidden, reason);
        }

        // 3. Check concurrent run quota
        var activeRuns = await _unitOfWork.AgentRuns.GetActiveByUserIdAsync(userId, cancellationToken);
        if (activeRuns.Count() >= DefaultMaxConcurrentRunsPerUser)
            return Result.Failure<AgentRunDto>(ErrorCodes.TooManyRequests,
                $"Maximum concurrent runs ({DefaultMaxConcurrentRunsPerUser}) exceeded.");

        // 4. Create the run
        var run = new AgentRun(agentProfileId, userId, objective, triggerType, boardId);
        await _unitOfWork.AgentRuns.AddAsync(run, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        AgentTelemetry.RecordRunStarted(triggerType, profile.TemplateKey);
        var sw = Stopwatch.StartNew();

        // 5. Record policy validation as first event
        var eventSeq = 0;
        var policyEvent = new AgentRunEvent(run.Id, eventSeq++, "policy.validated",
            System.Text.Json.JsonSerializer.Serialize(new
            {
                tools = bundleDecisions.Select(d => new { d.ToolKey, d.Allowed, d.Reason })
            }));
        await _unitOfWork.AgentRuns.AddEventAsync(policyEvent, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // 6. Execute steps
        try
        {
            for (var step = 0; step < maxSteps; step++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                AgentStepResult stepResult;
                try
                {
                    stepResult = await executeStep(run, step, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw; // re-throw to be caught by outer handler
                }
                catch (EgressViolationException)
                {
                    throw; // re-throw to be caught by outer handler
                }

                run.IncrementSteps();
                if (stepResult.TokensUsed > 0)
                    run.AddTokenUsage(stepResult.TokensUsed);

                AgentTelemetry.RecordStep(stepResult.EventType);

                // Record event
                var stepEvent = new AgentRunEvent(run.Id, eventSeq++, stepResult.EventType, stepResult.Payload);
                await _unitOfWork.AgentRuns.AddEventAsync(stepEvent, cancellationToken);

                if (stepResult.IsTerminal)
                {
                    if (stepResult.ProposalId.HasValue)
                    {
                        run.AttachProposal(stepResult.ProposalId.Value, stepResult.Summary);
                        run.TransitionTo(AgentRunStatus.ProposalCreated);
                        AgentTelemetry.RecordProposalCreated(profile.TemplateKey);
                    }
                    else
                    {
                        run.TransitionTo(AgentRunStatus.Completed, stepResult.Summary);
                    }

                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    sw.Stop();
                    AgentTelemetry.RecordRunCompleted(triggerType, profile.TemplateKey,
                        sw.Elapsed.TotalMilliseconds, run.StepsExecuted, run.TokensUsed);

                    return Result.Success(MapToDto(run));
                }

                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            // Step quota exhausted
            run.TransitionTo(AgentRunStatus.Completed, $"Step quota exhausted after {maxSteps} steps.");
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            sw.Stop();

            AgentTelemetry.RecordQuotaExceeded("steps", profile.TemplateKey);
            AgentTelemetry.RecordRunCompleted(triggerType, profile.TemplateKey,
                sw.Elapsed.TotalMilliseconds, run.StepsExecuted, run.TokensUsed);

            return Result.Success(MapToDto(run));
        }
        catch (OperationCanceledException)
        {
            run.TransitionTo(AgentRunStatus.Cancelled, "Run cancelled by user.");
            await _unitOfWork.SaveChangesAsync(CancellationToken.None);
            sw.Stop();
            AgentTelemetry.RecordRunCancelled(triggerType, profile.TemplateKey);
            return Result.Success(MapToDto(run));
        }
        catch (EgressViolationException egressEx)
        {
            run.MarkFailed($"Egress violation: {egressEx.Violation.AttemptedHost} — {egressEx.Violation.Reason}");
            await _unitOfWork.SaveChangesAsync(CancellationToken.None);
            sw.Stop();
            AgentTelemetry.RecordEgressViolation(egressEx.Violation.AttemptedHost, "unknown");
            AgentTelemetry.RecordRunFailed(triggerType, profile.TemplateKey);
            return Result.Success(MapToDto(run));
        }
        catch (Exception ex)
        {
            run.MarkFailed($"Unexpected error: {ex.Message}");
            await _unitOfWork.SaveChangesAsync(CancellationToken.None);
            sw.Stop();
            AgentTelemetry.RecordRunFailed(triggerType, profile.TemplateKey);
            return Result.Success(MapToDto(run));
        }
    }

    private static AgentRunDto MapToDto(AgentRun run)
    {
        return new AgentRunDto(
            Id: run.Id,
            AgentProfileId: run.AgentProfileId,
            UserId: run.UserId,
            BoardId: run.BoardId,
            TriggerType: run.TriggerType,
            Objective: run.Objective,
            Status: run.Status,
            Summary: run.Summary,
            FailureReason: run.FailureReason,
            ProposalId: run.ProposalId,
            StepsExecuted: run.StepsExecuted,
            TokensUsed: run.TokensUsed,
            ApproxCostUsd: run.ApproxCostUsd,
            StartedAt: run.StartedAt,
            CompletedAt: run.CompletedAt,
            CreatedAt: run.CreatedAt,
            UpdatedAt: run.UpdatedAt);
    }
}
