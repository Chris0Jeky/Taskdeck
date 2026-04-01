using System.Text.Json;
using Taskdeck.Application.Interfaces;

namespace Taskdeck.Application.Services.Tools;

/// <summary>
/// Executes the get_card_details tool: returns full card details including
/// description, labels, and dates for a specific card ID.
/// </summary>
public sealed class GetCardDetailsExecutor : IToolExecutor
{
    private readonly IUnitOfWork _unitOfWork;

    public GetCardDetailsExecutor(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public string ToolName => "get_card_details";

    public async Task<string> ExecuteAsync(Guid boardId, JsonElement arguments, CancellationToken ct = default)
    {
        var cardId = arguments.TryGetProperty("card_id", out var ci)
            ? ci.GetString() ?? ""
            : "";

        if (string.IsNullOrWhiteSpace(cardId))
        {
            return JsonSerializer.Serialize(new
            {
                error = "card_id is required",
                suggestion = "Use search_cards or list_cards_in_column to find card IDs"
            }, ToolJsonOptions.Default);
        }

        // Resolve short ID to full GUID
        var allCards = await _unitOfWork.Cards.GetByBoardIdAsync(boardId, ct);
        var card = allCards.FirstOrDefault(c =>
            BoardContextBuilder.FormatShortId(c.Id).Equals(cardId, StringComparison.OrdinalIgnoreCase));

        if (card == null)
        {
            return JsonSerializer.Serialize(new
            {
                error = $"Card {cardId} not found",
                suggestion = "Use search_cards to find the card"
            }, ToolJsonOptions.Default);
        }

        // Look up column name
        var columns = await _unitOfWork.Columns.GetByBoardIdAsync(boardId, ct);
        var column = columns.FirstOrDefault(c => c.Id == card.ColumnId);

        // Look up label names
        var labels = (await _unitOfWork.Labels.GetByBoardIdAsync(boardId, ct))
            .ToDictionary(l => l.Id, l => l.Name);

        var cardLabels = card.CardLabels
            .Where(cl => labels.ContainsKey(cl.LabelId))
            .Select(cl => labels[cl.LabelId])
            .ToArray();

        var result = new
        {
            id = BoardContextBuilder.FormatShortId(card.Id),
            title = card.Title,
            description = card.Description ?? "",
            column = column?.Name ?? "Unknown",
            labels = cardLabels,
            created_at = card.CreatedAt.ToString("O"),
            updated_at = card.UpdatedAt.ToString("O")
        };

        return JsonSerializer.Serialize(result, ToolJsonOptions.Default);
    }
}
