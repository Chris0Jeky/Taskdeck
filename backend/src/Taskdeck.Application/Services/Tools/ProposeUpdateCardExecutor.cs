using System.Text.Json;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services.Pipeline;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Services.Tools;

/// <summary>
/// Executes the propose_update_card tool: creates a proposal to update a card's
/// title, description, due date, or labels. Always produces a proposal (GP-06 compliance).
/// </summary>
public sealed class ProposeUpdateCardExecutor : IToolExecutor
{
    private readonly IAutomationProposalService _proposalService;
    private readonly IAutomationPolicyEngine _policyEngine;
    private readonly IUnitOfWork _unitOfWork;

    public ProposeUpdateCardExecutor(
        IAutomationProposalService proposalService,
        IAutomationPolicyEngine policyEngine,
        IUnitOfWork unitOfWork)
    {
        _proposalService = proposalService;
        _policyEngine = policyEngine;
        _unitOfWork = unitOfWork;
    }

    public string ToolName => "propose_update_card";

    public Task<string> ExecuteAsync(Guid boardId, JsonElement arguments, CancellationToken ct = default)
    {
        return Task.FromResult(JsonSerializer.Serialize(new
        {
            error = "propose_update_card requires user context",
            suggestion = "This is an internal error; please try again"
        }, ToolJsonOptions.Default));
    }

    public async Task<string> ExecuteAsync(ToolExecutionContext context, JsonElement arguments, CancellationToken ct = default)
    {
        var cardIdStr = arguments.TryGetProperty("card_id", out var ci) ? ci.GetString() ?? "" : "";

        if (string.IsNullOrWhiteSpace(cardIdStr))
        {
            return JsonSerializer.Serialize(new
            {
                error = "card_id is required",
                suggestion = "Use search_cards or list_cards_in_column to find card IDs"
            }, ToolJsonOptions.Default);
        }

        var newTitle = arguments.TryGetProperty("title", out var t) ? t.GetString() : null;
        var newDescription = arguments.TryGetProperty("description", out var d) ? d.GetString() : null;
        if (!OperationParameterParser.TryGetOptionalStringArray(
                arguments, "labels", out var hasLabels, out var newLabels, out var labelsError))
        {
            return JsonSerializer.Serialize(new
            {
                error = labelsError,
                suggestion = "Provide labels as an array of non-empty names"
            }, ToolJsonOptions.Default);
        }

        if (!OperationParameterParser.TryGetOptionalDateTimeOffset(
                arguments, "due_date", out var hasDueDate, out var newDueDate, out var dueDateError))
        {
            return JsonSerializer.Serialize(new
            {
                error = dueDateError,
                suggestion = "Use YYYY-MM-DD or an ISO-8601 timestamp with an offset"
            }, ToolJsonOptions.Default);
        }

        var clearDueDate = false;
        if (arguments.TryGetProperty("clear_due_date", out var clearDueDateProperty))
        {
            if (clearDueDateProperty.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return JsonSerializer.Serialize(new
                {
                    error = "clear_due_date must be a boolean",
                    suggestion = "Set clear_due_date to true to remove the current due date"
                }, ToolJsonOptions.Default);
            }

            clearDueDate = clearDueDateProperty.GetBoolean();
        }

        if (newDueDate.HasValue && clearDueDate)
        {
            return JsonSerializer.Serialize(new
            {
                error = "due_date and clear_due_date cannot both be set",
                suggestion = "Choose a new due date or clear the current one"
            }, ToolJsonOptions.Default);
        }

        // At least one field must be provided
        if (string.IsNullOrWhiteSpace(newTitle) && newDescription == null && !hasLabels && !hasDueDate && !clearDueDate)
        {
            return JsonSerializer.Serialize(new
            {
                error = "At least one field (title, description, due_date, clear_due_date, or labels) must be provided",
                suggestion = "Specify what to update on the card"
            }, ToolJsonOptions.Default);
        }

        // Resolve short ID to full card
        var allCards = await _unitOfWork.Cards.GetByBoardIdAsync(context.BoardId, ct);
        var card = allCards.FirstOrDefault(c =>
            BoardContextBuilder.FormatShortId(c.Id).Equals(cardIdStr, StringComparison.OrdinalIgnoreCase));

        if (card == null)
        {
            return JsonSerializer.Serialize(new
            {
                error = $"Card {cardIdStr} not found",
                suggestion = "Use search_cards to find the card"
            }, ToolJsonOptions.Default);
        }

        // Build update parameters — only include fields that were provided
        var updateParams = new Dictionary<string, object> { { "cardId", card.Id } };

        if (!string.IsNullOrWhiteSpace(newTitle))
            updateParams["title"] = newTitle;
        if (newDescription != null)
            updateParams["description"] = newDescription;
        if (newDueDate.HasValue)
            updateParams["dueDate"] = newDueDate.Value.ToString("O");
        if (clearDueDate || (hasDueDate && !newDueDate.HasValue))
            updateParams["clearDueDate"] = true;
        if (hasLabels)
            updateParams["labels"] = newLabels;

        var parameters = JsonSerializer.Serialize(updateParams);

        var operations = new List<CreateProposalOperationDto>
        {
            new(0, "update", "card", parameters, Guid.NewGuid().ToString(), TargetId: card.Id.ToString())
        };

        var operationDtos = operations.Select(o => new ProposalOperationDto(
            Guid.Empty, Guid.Empty, o.Sequence, o.ActionType,
            o.TargetType, o.TargetId, o.Parameters, o.IdempotencyKey, o.ExpectedVersion
        )).ToList();

        var riskLevel = _policyEngine.ClassifyRisk(operationDtos);

        // Build summary describing what is being updated
        var updateParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(newTitle)) updateParts.Add($"title to '{newTitle}'");
        if (newDescription != null) updateParts.Add("description");
        if (newDueDate.HasValue) updateParts.Add($"due date to {newDueDate.Value:yyyy-MM-dd}");
        if (clearDueDate || (hasDueDate && !newDueDate.HasValue)) updateParts.Add("clear due date");
        if (hasLabels) updateParts.Add($"labels to [{string.Join(", ", newLabels)}]");
        var summary = $"Update card '{card.Title}': {string.Join(", ", updateParts)}";
        if (summary.Length > 500) summary = summary[..497] + "...";

        var createDto = new CreateProposalDto(
            ProposalSourceType.Chat,
            context.UserId,
            summary,
            riskLevel,
            Guid.NewGuid().ToString(),
            context.BoardId,
            null,
            1440,
            operations
        );

        var result = await _proposalService.CreateProposalAsync(createDto, ct);
        if (!result.IsSuccess)
        {
            return JsonSerializer.Serialize(new
            {
                error = $"Failed to create proposal: {result.ErrorMessage}"
            }, ToolJsonOptions.Default);
        }

        return JsonSerializer.Serialize(new
        {
            proposal_id = BoardContextBuilder.FormatShortId(result.Value.Id),
            full_proposal_id = result.Value.Id,
            summary,
            risk = riskLevel.ToString()
        }, ToolJsonOptions.Default);
    }

}
