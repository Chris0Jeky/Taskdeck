using System.Text.Json;
using Microsoft.Extensions.Logging;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.Services;

/// <summary>
/// Result DTO for an inbox triage digest run.
/// </summary>
public record InboxDigestResultDto(
    Guid RunId,
    Guid? ProposalId,
    int ItemsProcessed,
    string Status);

/// <summary>
/// Scheduled bounded agent that coalesces pending inbox items into a single
/// digest proposal. Runs behind the Agent:InboxTriageDigest:Enabled feature flag.
/// Creates only proposals — never directly mutates boards (GP-06).
/// Records an inspectable trace via AgentRuntime (GP-09).
/// </summary>
public sealed class InboxTriageDigestAgent
{
    public const string AgentTemplateKey = "inbox-triage-digest";
    public const string FeatureFlagKey = "Agent:InboxTriageDigest:Enabled";
    public const int MaxItemsPerDigest = 50;
    public const int MaxDigestsPerDay = 10;

    private readonly AgentRuntime _runtime;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAutomationProposalService _proposalService;
    private readonly ILogger<InboxTriageDigestAgent>? _logger;

    public InboxTriageDigestAgent(
        AgentRuntime runtime,
        IUnitOfWork unitOfWork,
        IAutomationProposalService proposalService,
        ILogger<InboxTriageDigestAgent>? logger = null)
    {
        _runtime = runtime;
        _unitOfWork = unitOfWork;
        _proposalService = proposalService;
        _logger = logger;
    }

    /// <summary>
    /// Runs the inbox triage digest for a user. Gathers pending inbox items,
    /// creates a coalesced proposal, and records the full run trace.
    /// </summary>
    public async Task<InboxDigestResultDto> RunDigestAsync(
        Guid agentProfileId,
        Guid userId,
        Guid boardId,
        string triggerType = "manual",
        CancellationToken cancellationToken = default)
    {
        // Gather pending items ahead of the run
        var pendingItems = (await _unitOfWork.LlmQueue.GetByUserAsync(userId, cancellationToken))
            .Where(r => r.Status == RequestStatus.Pending)
            .OrderBy(r => r.CreatedAt)
            .Take(MaxItemsPerDigest)
            .ToList();

        if (pendingItems.Count == 0)
        {
            _logger?.LogInformation("No pending inbox items for user {UserId}", userId);
            return new InboxDigestResultDto(Guid.Empty, null, 0, "NoItems");
        }

        // Verify board and columns exist
        var board = await _unitOfWork.Boards.GetByIdAsync(boardId, cancellationToken);
        if (board is null)
        {
            _logger?.LogWarning("Board {BoardId} not found for digest", boardId);
            return new InboxDigestResultDto(Guid.Empty, null, 0, "BoardNotFound");
        }

        var columns = (await _unitOfWork.Columns.GetByBoardIdAsync(boardId, cancellationToken))
            .OrderBy(c => c.Position)
            .ToList();

        if (columns.Count == 0)
        {
            _logger?.LogWarning("Board {BoardId} has no columns", boardId);
            return new InboxDigestResultDto(Guid.Empty, null, 0, "NoColumns");
        }

        var defaultColumnId = columns[0].Id;
        Guid? proposalId = null;

        var result = await _runtime.RunAsync(
            agentProfileId,
            userId,
            $"Digest triage of {pendingItems.Count} inbox items",
            new[] { InboxTriageAssistant.ToolKey },
            async (run, step, ct) =>
            {
                // Build proposal operations from pending items
                var operations = pendingItems.Select((item, i) =>
                {
                    var parameters = JsonSerializer.Serialize(new
                    {
                        title = TruncateTitle(item.Payload),
                        description = $"Digest triaged from inbox item {item.Id}",
                        columnId = defaultColumnId,
                        boardId
                    });

                    return new CreateProposalOperationDto(
                        Sequence: i,
                        ActionType: "create",
                        TargetType: "card",
                        Parameters: parameters,
                        IdempotencyKey: $"digest:{item.Id:N}:{boardId:N}");
                }).ToList();

                var summary = $"Inbox digest: {pendingItems.Count} items for board '{board.Name}'";

                var createResult = await _proposalService.CreateProposalAsync(
                    new CreateProposalDto(
                        SourceType: ProposalSourceType.Queue,
                        RequestedByUserId: userId,
                        Summary: summary,
                        RiskLevel: RiskLevel.Low,
                        CorrelationId: Guid.NewGuid().ToString(),
                        BoardId: boardId,
                        Operations: operations),
                    ct);

                if (!createResult.IsSuccess)
                {
                    return new AgentStepResult(
                        EventType: "digest.proposal_failed",
                        Payload: JsonSerializer.Serialize(new { error = createResult.ErrorMessage }),
                        IsTerminal: true,
                        Summary: $"Failed to create proposal: {createResult.ErrorMessage}");
                }

                proposalId = createResult.Value.Id;

                return new AgentStepResult(
                    EventType: "digest.proposal_created",
                    Payload: JsonSerializer.Serialize(new
                    {
                        proposalId = createResult.Value.Id,
                        itemCount = pendingItems.Count
                    }),
                    IsTerminal: true,
                    ProposalId: createResult.Value.Id,
                    Summary: summary);
            },
            triggerType: triggerType,
            boardId: boardId,
            cancellationToken: cancellationToken);

        if (!result.IsSuccess)
        {
            _logger?.LogWarning("Digest run failed: {Error}", result.ErrorMessage);
            return new InboxDigestResultDto(Guid.Empty, null, 0, result.ErrorCode);
        }

        return new InboxDigestResultDto(
            result.Value.Id,
            proposalId,
            pendingItems.Count,
            result.Value.Status.ToString());
    }

    private static string TruncateTitle(string input)
    {
        const int maxLength = 200;
        var firstLine = input.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? input;
        var trimmed = firstLine.Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength].TrimEnd() : trimmed;
    }
}
