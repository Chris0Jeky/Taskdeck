using System.Text.Json;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Services.Tools;

/// <summary>
/// Executes the propose_bulk_move tool: creates a proposal to move multiple cards
/// between columns. Risk level: High. Max 50 cards.
/// Always produces a proposal (GP-06 compliance — never direct mutation).
/// </summary>
public sealed class ProposeBulkMoveExecutor : IToolExecutor
{
    private const int MaxBulkCards = 50;

    private readonly IAutomationProposalService _proposalService;
    private readonly IAutomationPolicyEngine _policyEngine;
    private readonly IUnitOfWork _unitOfWork;

    public ProposeBulkMoveExecutor(
        IAutomationProposalService proposalService,
        IAutomationPolicyEngine policyEngine,
        IUnitOfWork unitOfWork)
    {
        _proposalService = proposalService;
        _policyEngine = policyEngine;
        _unitOfWork = unitOfWork;
    }

    public string ToolName => "propose_bulk_move";

    public Task<string> ExecuteAsync(Guid boardId, JsonElement arguments, CancellationToken ct = default)
    {
        return Task.FromResult(JsonSerializer.Serialize(new
        {
            error = "propose_bulk_move requires user context",
            suggestion = "This is an internal error; please try again"
        }, ToolJsonOptions.Default));
    }

    public async Task<string> ExecuteAsync(ToolExecutionContext context, JsonElement arguments, CancellationToken ct = default)
    {
        var sourceColumnName = arguments.TryGetProperty("source_column", out var sc) ? sc.GetString() ?? "" : "";
        var targetColumnName = arguments.TryGetProperty("target_column", out var tc) ? tc.GetString() ?? "" : "";
        var cardIdStrs = ExtractStringArray(arguments, "card_ids");

        if (string.IsNullOrWhiteSpace(sourceColumnName))
        {
            return JsonSerializer.Serialize(new
            {
                error = "source_column is required",
                suggestion = "Use list_board_columns to see available columns"
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

        // Resolve columns
        var columns = await _unitOfWork.Columns.GetByBoardIdAsync(context.BoardId, ct);
        var sourceColumn = columns.FirstOrDefault(c =>
            string.Equals(c.Name, sourceColumnName, StringComparison.OrdinalIgnoreCase));

        if (sourceColumn == null)
        {
            var availableNames = columns.Select(c => c.Name).ToArray();
            return JsonSerializer.Serialize(new
            {
                error = $"Source column '{sourceColumnName}' not found",
                suggestion = "Use list_board_columns to see available columns",
                available_columns = availableNames
            }, ToolJsonOptions.Default);
        }

        var targetColumn = columns.FirstOrDefault(c =>
            string.Equals(c.Name, targetColumnName, StringComparison.OrdinalIgnoreCase));

        if (targetColumn == null)
        {
            var availableNames = columns.Select(c => c.Name).ToArray();
            return JsonSerializer.Serialize(new
            {
                error = $"Target column '{targetColumnName}' not found",
                suggestion = "Use list_board_columns to see available columns",
                available_columns = availableNames
            }, ToolJsonOptions.Default);
        }

        // Get cards to move
        var allCardsInSource = (await _unitOfWork.Cards.GetByColumnIdAsync(sourceColumn.Id, ct)).ToList();
        List<Domain.Entities.Card> cardsToMove;

        if (cardIdStrs.Length > 0)
        {
            // Resolve specific card IDs from board
            var allBoardCards = await _unitOfWork.Cards.GetByBoardIdAsync(context.BoardId, ct);
            var cardLookup = allBoardCards
                .GroupBy(c => BoardContextBuilder.FormatShortId(c.Id).ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            cardsToMove = new List<Domain.Entities.Card>();
            var notFound = new List<string>();

            foreach (var shortId in cardIdStrs)
            {
                if (cardLookup.TryGetValue(shortId.ToLowerInvariant(), out var card))
                {
                    cardsToMove.Add(card);
                }
                else
                {
                    notFound.Add(shortId);
                }
            }

            if (notFound.Count > 0)
            {
                return JsonSerializer.Serialize(new
                {
                    error = $"Cards not found: {string.Join(", ", notFound)}",
                    suggestion = "Use search_cards or list_cards_in_column to find valid card IDs"
                }, ToolJsonOptions.Default);
            }
        }
        else
        {
            // Move all cards in source column
            cardsToMove = allCardsInSource;
        }

        if (cardsToMove.Count == 0)
        {
            return JsonSerializer.Serialize(new
            {
                error = $"No cards to move in '{sourceColumn.Name}'",
                suggestion = "The source column is empty"
            }, ToolJsonOptions.Default);
        }

        if (cardsToMove.Count > MaxBulkCards)
        {
            return JsonSerializer.Serialize(new
            {
                error = $"Too many cards ({cardsToMove.Count}) to move at once. Maximum is {MaxBulkCards}.",
                suggestion = "Specify specific card_ids to move a subset, or split into multiple operations"
            }, ToolJsonOptions.Default);
        }

        // Build operations — one move operation per card
        var operations = cardsToMove.Select((card, i) =>
        {
            var parameters = JsonSerializer.Serialize(new
            {
                cardId = card.Id,
                columnId = targetColumn.Id
            });
            return new CreateProposalOperationDto(
                i, "move", "card", parameters, Guid.NewGuid().ToString(), TargetId: card.Id.ToString());
        }).ToList();

        var operationDtos = operations.Select(o => new ProposalOperationDto(
            Guid.Empty, Guid.Empty, o.Sequence, o.ActionType,
            o.TargetType, o.TargetId, o.Parameters, o.IdempotencyKey, o.ExpectedVersion
        )).ToList();

        var riskLevel = _policyEngine.ClassifyRisk(operationDtos);

        var summary = $"Move {cardsToMove.Count} card{(cardsToMove.Count == 1 ? "" : "s")} from {sourceColumn.Name} to {targetColumn.Name}";
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
            risk = riskLevel.ToString(),
            card_count = cardsToMove.Count
        }, ToolJsonOptions.Default);
    }

    private static string[] ExtractStringArray(JsonElement args, string propertyName)
    {
        if (!args.TryGetProperty(propertyName, out var prop) || prop.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        return prop.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArray();
    }
}
