using System.Text.Json;
using Microsoft.Extensions.Logging;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Agents;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

/// <summary>
/// Bounded agent template that triages inbox items into proposals.
/// Never directly mutates board state — all changes are routed through
/// the proposal system and policy evaluator.
/// </summary>
public sealed class InboxTriageAssistant
{
    /// <summary>Tool key for the inbox triage tool registered in the tool registry.</summary>
    public const string ToolKey = "inbox.triage";

    /// <summary>Maximum number of inbox items to gather in a single triage run.</summary>
    private const int MaxInboxItemsPerRun = 20;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAgentPolicyEvaluator _policyEvaluator;
    private readonly IAutomationProposalService _proposalService;
    private readonly ILogger<InboxTriageAssistant>? _logger;

    public InboxTriageAssistant(
        IUnitOfWork unitOfWork,
        IAgentPolicyEvaluator policyEvaluator,
        IAutomationProposalService proposalService,
        ILogger<InboxTriageAssistant>? logger = null)
    {
        _unitOfWork = unitOfWork;
        _policyEvaluator = policyEvaluator;
        _proposalService = proposalService;
        _logger = logger;
    }

    /// <summary>
    /// Run the inbox triage template for a given agent profile and board.
    /// Gathers pending inbox items, evaluates policy, and creates a proposal
    /// for triage actions. Returns a failure result if policy denies the action
    /// or if no actionable items are found.
    /// </summary>
    public async Task<Result<InboxTriageResultDto>> RunTriageAsync(
        Guid agentProfileId,
        Guid userId,
        Guid boardId,
        CancellationToken cancellationToken = default)
    {
        if (agentProfileId == Guid.Empty)
            return Result.Failure<InboxTriageResultDto>(ErrorCodes.ValidationError, "Agent profile ID is required.");

        if (userId == Guid.Empty)
            return Result.Failure<InboxTriageResultDto>(ErrorCodes.ValidationError, "User ID is required.");

        if (boardId == Guid.Empty)
            return Result.Failure<InboxTriageResultDto>(ErrorCodes.ValidationError, "Board ID is required.");

        // Evaluate policy before proceeding
        var policyDecision = await _policyEvaluator.EvaluateToolUseAsync(
            agentProfileId,
            ToolKey,
            new Dictionary<string, string> { ["boardId"] = boardId.ToString() },
            cancellationToken);

        if (!policyDecision.Allowed)
        {
            _logger?.LogInformation(
                "Inbox triage denied by policy for profile '{ProfileId}': {Reason}",
                agentProfileId, policyDecision.Reason);
            return Result.Failure<InboxTriageResultDto>(ErrorCodes.Forbidden, policyDecision.Reason);
        }

        // Gather inbox context: recent pending items for this user
        var pendingItems = (await _unitOfWork.LlmQueue.GetByUserAsync(userId, cancellationToken))
            .Where(r => r.Status == RequestStatus.Pending)
            .OrderBy(r => r.CreatedAt)
            .Take(MaxInboxItemsPerRun)
            .ToList();

        if (pendingItems.Count == 0)
        {
            _logger?.LogInformation("Inbox triage found no pending items for user '{UserId}'", userId);
            return Result.Failure<InboxTriageResultDto>(
                ErrorCodes.NotFound, "No pending inbox items to triage.");
        }

        // Verify board exists
        var board = await _unitOfWork.Boards.GetByIdAsync(boardId, cancellationToken);
        if (board is null)
        {
            return Result.Failure<InboxTriageResultDto>(
                ErrorCodes.NotFound, $"Board '{boardId}' not found.");
        }

        // Get the first column to use as the default target
        var columns = (await _unitOfWork.Columns.GetByBoardIdAsync(boardId, cancellationToken))
            .OrderBy(c => c.Position)
            .ToList();

        if (columns.Count == 0)
        {
            return Result.Failure<InboxTriageResultDto>(
                ErrorCodes.NotFound, "Board has no columns to triage into.");
        }

        var defaultColumnId = columns[0].Id;

        // Build proposal operations — one create-card per inbox item
        var operations = pendingItems.Select((item, i) =>
        {
            var parameters = JsonSerializer.Serialize(new
            {
                title = TruncateTitle(item.Payload),
                description = $"Triaged from inbox item {item.Id}",
                columnId = defaultColumnId,
                boardId
            });

            return new CreateProposalOperationDto(
                Sequence: i,
                ActionType: "create",
                TargetType: "card",
                Parameters: parameters,
                IdempotencyKey: $"inbox-triage:{item.Id:N}:{boardId:N}");
        }).ToList();

        // Create the proposal — never directly mutating the board
        var summary = pendingItems.Count == 1
            ? $"Inbox triage: 1 item for board '{board.Name}'"
            : $"Inbox triage: {pendingItems.Count} items for board '{board.Name}'";

        var createResult = await _proposalService.CreateProposalAsync(
            new CreateProposalDto(
                SourceType: ProposalSourceType.Queue,
                RequestedByUserId: userId,
                Summary: summary,
                RiskLevel: RiskLevel.Low,
                CorrelationId: Guid.NewGuid().ToString(),
                BoardId: boardId,
                Operations: operations),
            cancellationToken);

        if (!createResult.IsSuccess)
        {
            _logger?.LogWarning(
                "Inbox triage proposal creation failed for profile '{ProfileId}': {Error}",
                agentProfileId, createResult.ErrorMessage);
            return Result.Failure<InboxTriageResultDto>(createResult.ErrorCode, createResult.ErrorMessage);
        }

        _logger?.LogInformation(
            "Inbox triage created proposal '{ProposalId}' with {Count} operations (review required: {Review})",
            createResult.Value.Id, operations.Count, policyDecision.RequiresReview);

        return Result.Success(new InboxTriageResultDto(
            createResult.Value.Id,
            operations.Count,
            policyDecision.RequiresReview,
            policyDecision.Reason));
    }

    private static string TruncateTitle(string input)
    {
        const int maxLength = 200;
        var firstLine = input.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? input;
        var trimmed = firstLine.Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength].TrimEnd() : trimmed;
    }

    /// <summary>
    /// Returns the built-in tool definition for registration in the tool registry.
    /// </summary>
    public static ITaskdeckTool GetToolDefinition()
    {
        return new TaskdeckToolDefinition(
            Key: ToolKey,
            DisplayName: "Inbox Triage",
            Description: "Triages pending inbox items into card proposals for a target board.",
            Scope: ToolScope.Inbox,
            RiskLevel: ToolRiskLevel.Medium);
    }
}

/// <summary>
/// Result DTO for an inbox triage run.
/// </summary>
public record InboxTriageResultDto(
    Guid ProposalId,
    int ItemsTriaged,
    bool RequiresReview,
    string PolicyReason);
