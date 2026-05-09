using System.Text.Json;
using Microsoft.Extensions.Logging;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

/// <summary>
/// First scheduled bounded agent. Coalesces pending inbox items into a single
/// triage digest proposal per board. Behind a feature flag.
///
/// Key constraints:
/// - Creates only proposals, never directly mutates boards (GP-06)
/// - Records inspectable trace via AgentRunEvent records
/// - Quota guardrails: max items per digest, max digests per day
/// - Can be triggered manually or on schedule
/// </summary>
public sealed class InboxTriageDigestAgent
{
    public const string AgentTemplateKey = "inbox-triage-digest";
    public const string FeatureFlagKey = "Agent:InboxTriageDigest:Enabled";

    /// <summary>Maximum inbox items to include per digest proposal.</summary>
    public const int MaxItemsPerDigest = 50;

    /// <summary>Maximum digest proposals per user per 24-hour window.</summary>
    public const int MaxDigestsPerDay = 10;

    private readonly IUnitOfWork _unitOfWork;
    private readonly AgentRuntime _runtime;
    private readonly IAutomationProposalService _proposalService;
    private readonly ILogger<InboxTriageDigestAgent>? _logger;

    public InboxTriageDigestAgent(
        IUnitOfWork unitOfWork,
        AgentRuntime runtime,
        IAutomationProposalService proposalService,
        ILogger<InboxTriageDigestAgent>? logger = null)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _proposalService = proposalService ?? throw new ArgumentNullException(nameof(proposalService));
        _logger = logger;
    }

    /// <summary>
    /// Run the inbox triage digest for a user, creating a coalesced proposal
    /// for all pending inbox items across their boards.
    /// </summary>
    public async Task<Result<InboxDigestResultDto>> RunDigestAsync(
        Guid agentProfileId,
        Guid userId,
        Guid boardId,
        string triggerType = "manual",
        CancellationToken cancellationToken = default)
    {
        if (agentProfileId == Guid.Empty)
            return Result.Failure<InboxDigestResultDto>(ErrorCodes.ValidationError, "Agent profile ID is required.");
        if (userId == Guid.Empty)
            return Result.Failure<InboxDigestResultDto>(ErrorCodes.ValidationError, "User ID is required.");
        if (boardId == Guid.Empty)
            return Result.Failure<InboxDigestResultDto>(ErrorCodes.ValidationError, "Board ID is required.");

        // Check daily quota across all trigger types and profiles for this user
        var todayRuns = await _unitOfWork.AgentRuns.CountRecentByUserIdAsync(
            userId, DateTimeOffset.UtcNow.AddHours(-24), cancellationToken);

        if (todayRuns >= MaxDigestsPerDay)
        {
            _logger?.LogWarning(
                "Inbox digest daily quota exceeded for profile '{ProfileId}' ({Count}/{Max})",
                agentProfileId, todayRuns, MaxDigestsPerDay);
            return Result.Failure<InboxDigestResultDto>(ErrorCodes.TooManyRequests,
                $"Maximum of {MaxDigestsPerDay} inbox digests per 24 hours.");
        }

        // Gather pending inbox items (use status-filtered query to avoid loading all items in memory)
        var pendingItems = (await _unitOfWork.LlmQueue.GetByUserAndStatusAsync(userId, RequestStatus.Pending, cancellationToken))
            .OrderBy(r => r.CreatedAt)
            .Take(MaxItemsPerDigest)
            .ToList();

        if (pendingItems.Count == 0)
        {
            _logger?.LogInformation("Inbox digest found no pending items for user '{UserId}'", userId);
            return Result.Failure<InboxDigestResultDto>(ErrorCodes.NotFound, "No pending inbox items to digest.");
        }

        // Verify board exists and get columns
        var board = await _unitOfWork.Boards.GetByIdAsync(boardId, cancellationToken);
        if (board is null)
            return Result.Failure<InboxDigestResultDto>(ErrorCodes.NotFound, $"Board '{boardId}' not found.");

        var columns = (await _unitOfWork.Columns.GetByBoardIdAsync(boardId, cancellationToken))
            .OrderBy(c => c.Position)
            .ToList();

        if (columns.Count == 0)
            return Result.Failure<InboxDigestResultDto>(ErrorCodes.NotFound, "Board has no columns to triage into.");

        var defaultColumnId = columns[0].Id;

        // Use the AgentRuntime for execution with full policy and quota tracking
        var requestedTools = new List<string> { InboxTriageAssistant.ToolKey };

        var runtimeResult = await _runtime.RunAsync(
            agentProfileId,
            userId,
            $"Inbox triage digest: {pendingItems.Count} items for board '{board.Name}'",
            requestedTools,
            executeStep: async (run, step, ct) =>
            {
                if (step == 0)
                {
                    // First step: create the coalesced proposal
                    var operations = pendingItems.Select((item, i) =>
                    {
                        var parameters = JsonSerializer.Serialize(new
                        {
                            title = TruncateTitle(item.Payload),
                            description = $"Triaged from inbox item {item.Id} (digest run {run.Id})",
                            columnId = defaultColumnId,
                            boardId
                        });

                        return new CreateProposalOperationDto(
                            Sequence: i,
                            ActionType: "create",
                            TargetType: "card",
                            Parameters: parameters,
                            IdempotencyKey: $"digest:{run.Id:N}:{item.Id:N}");
                    }).ToList();

                    var summary = pendingItems.Count == 1
                        ? $"Inbox digest: 1 item for board '{board.Name}'"
                        : $"Inbox digest: {pendingItems.Count} items for board '{board.Name}'";

                    var createResult = await _proposalService.CreateProposalAsync(
                        new CreateProposalDto(
                            SourceType: ProposalSourceType.Queue,
                            RequestedByUserId: userId,
                            Summary: summary,
                            RiskLevel: RiskLevel.Low,
                            CorrelationId: run.Id.ToString(),
                            BoardId: boardId,
                            Operations: operations),
                        ct);

                    if (!createResult.IsSuccess)
                    {
                        return new AgentStepResult(
                            EventType: "digest.proposal_failed",
                            Payload: JsonSerializer.Serialize(new { error = createResult.ErrorMessage }),
                            IsTerminal: true,
                            Summary: $"Proposal creation failed: {createResult.ErrorMessage}");
                    }

                    return new AgentStepResult(
                        EventType: "digest.proposal_created",
                        Payload: JsonSerializer.Serialize(new
                        {
                            proposalId = createResult.Value.Id,
                            itemCount = pendingItems.Count,
                            boardId,
                            boardName = board.Name
                        }),
                        IsTerminal: true,
                        ProposalId: createResult.Value.Id,
                        Summary: summary);
                }

                return new AgentStepResult("digest.noop", IsTerminal: true);
            },
            triggerType: triggerType,
            boardId: boardId,
            maxSteps: 1,
            maxTokens: 4096,
            cancellationToken: cancellationToken);

        if (!runtimeResult.IsSuccess)
            return Result.Failure<InboxDigestResultDto>(runtimeResult.ErrorCode, runtimeResult.ErrorMessage);

        return Result.Success(new InboxDigestResultDto(
            RunId: runtimeResult.Value.Id,
            ProposalId: runtimeResult.Value.ProposalId,
            ItemsCoalesced: pendingItems.Count,
            TriggerType: triggerType,
            Summary: runtimeResult.Value.Summary));
    }

    private static string TruncateTitle(string input)
    {
        const int maxLength = 200;
        var firstLine = input.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? input;
        var trimmed = firstLine.Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength].TrimEnd() : trimmed;
    }
}

/// <summary>
/// Result DTO for an inbox triage digest run.
/// </summary>
public sealed record InboxDigestResultDto(
    Guid RunId,
    Guid? ProposalId,
    int ItemsCoalesced,
    string TriggerType,
    string? Summary);
