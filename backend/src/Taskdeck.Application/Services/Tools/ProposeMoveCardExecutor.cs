using System.Text.Json;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Services.Tools;

/// <summary>
/// Executes the propose_move_card tool: creates a proposal to move a card to a different column.
/// Always produces a proposal (GP-06 compliance — never direct mutation).
/// </summary>
public sealed class ProposeMoveCardExecutor : IToolExecutor
{
    private readonly IAutomationProposalService _proposalService;
    private readonly IAutomationPolicyEngine _policyEngine;
    private readonly IUnitOfWork _unitOfWork;

    public ProposeMoveCardExecutor(
        IAutomationProposalService proposalService,
        IAutomationPolicyEngine policyEngine,
        IUnitOfWork unitOfWork)
    {
        _proposalService = proposalService;
        _policyEngine = policyEngine;
        _unitOfWork = unitOfWork;
    }

    public string ToolName => "propose_move_card";

    public Task<string> ExecuteAsync(Guid boardId, JsonElement arguments, CancellationToken ct = default)
    {
        return Task.FromResult(JsonSerializer.Serialize(new
        {
            error = "propose_move_card requires user context",
            suggestion = "This is an internal error; please try again"
        }, ToolJsonOptions.Default));
    }

    public async Task<string> ExecuteAsync(ToolExecutionContext context, JsonElement arguments, CancellationToken ct = default)
    {
        var cardIdStr = arguments.TryGetProperty("card_id", out var ci) ? ci.GetString() ?? "" : "";
        var targetColumnName = arguments.TryGetProperty("target_column", out var tc) ? tc.GetString() ?? "" : "";

        if (string.IsNullOrWhiteSpace(cardIdStr))
        {
            return JsonSerializer.Serialize(new
            {
                error = "card_id is required",
                suggestion = "Use search_cards or list_cards_in_column to find card IDs"
            }, ToolJsonOptions.Default);
        }

        if (string.IsNullOrWhiteSpace(targetColumnName))
        {
            return JsonSerializer.Serialize(new
            {
                error = "target_column is required",
                suggestion = "Use list_board_columns to see available columns"
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

        // Resolve target column
        var columns = await _unitOfWork.Columns.GetByBoardIdAsync(context.BoardId, ct);
        var targetColumn = columns.FirstOrDefault(c =>
            string.Equals(c.Name, targetColumnName, StringComparison.OrdinalIgnoreCase));

        if (targetColumn == null)
        {
            var availableNames = columns.Select(c => c.Name).ToArray();
            return JsonSerializer.Serialize(new
            {
                error = $"Column '{targetColumnName}' not found",
                suggestion = "Use list_board_columns to see available columns",
                available_columns = availableNames
            }, ToolJsonOptions.Default);
        }

        var parameters = JsonSerializer.Serialize(new
        {
            cardId = card.Id,
            columnId = targetColumn.Id
        });

        var operations = new List<CreateProposalOperationDto>
        {
            new(0, "move", "card", parameters, Guid.NewGuid().ToString(), TargetId: card.Id.ToString())
        };

        var operationDtos = operations.Select(o => new ProposalOperationDto(
            Guid.Empty, Guid.Empty, o.Sequence, o.ActionType,
            o.TargetType, o.TargetId, o.Parameters, o.IdempotencyKey, o.ExpectedVersion
        )).ToList();

        var riskLevel = _policyEngine.ClassifyRisk(operationDtos);

        var createDto = new CreateProposalDto(
            ProposalSourceType.Chat,
            context.UserId,
            $"Move card '{card.Title}' to {targetColumn.Name}",
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
            summary = $"Move card '{card.Title}' to {targetColumn.Name}",
            risk = riskLevel.ToString()
        }, ToolJsonOptions.Default);
    }
}
