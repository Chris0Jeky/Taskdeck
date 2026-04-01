using System.Text.Json;
using Taskdeck.Application.Interfaces;

namespace Taskdeck.Application.Services.Tools;

/// <summary>
/// Executes the list_cards_in_column tool: returns cards in a named column
/// with IDs, titles, and labels. Max 20 cards with truncation indicator.
/// </summary>
public sealed class ListCardsInColumnExecutor : IToolExecutor
{
    private const int MaxCards = 20;

    private readonly IUnitOfWork _unitOfWork;

    public ListCardsInColumnExecutor(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public string ToolName => "list_cards_in_column";

    public async Task<string> ExecuteAsync(Guid boardId, JsonElement arguments, CancellationToken ct = default)
    {
        var columnName = arguments.TryGetProperty("column_name", out var cn)
            ? cn.GetString() ?? ""
            : "";

        if (string.IsNullOrWhiteSpace(columnName))
        {
            return JsonSerializer.Serialize(new
            {
                error = "column_name is required",
                suggestion = "Use list_board_columns to see available columns"
            }, ToolJsonOptions.Default);
        }

        var columns = await _unitOfWork.Columns.GetByBoardIdAsync(boardId, ct);
        var column = columns.FirstOrDefault(c =>
            string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase));

        if (column == null)
        {
            var availableNames = columns.Select(c => c.Name).ToArray();
            return JsonSerializer.Serialize(new
            {
                error = $"Column '{columnName}' not found",
                suggestion = "Use list_board_columns to see available columns",
                available_columns = availableNames
            }, ToolJsonOptions.Default);
        }

        var allCards = (await _unitOfWork.Cards.GetByColumnIdAsync(column.Id, ct)).ToList();
        var labels = (await _unitOfWork.Labels.GetByBoardIdAsync(boardId, ct))
            .ToDictionary(l => l.Id, l => l.Name);

        var total = allCards.Count;
        var truncated = total > MaxCards;
        var cards = allCards
            .OrderByDescending(c => c.UpdatedAt)
            .Take(MaxCards)
            .Select(c => new
            {
                id = BoardContextBuilder.FormatShortId(c.Id),
                title = c.Title,
                labels = c.CardLabels
                    .Where(cl => labels.ContainsKey(cl.LabelId))
                    .Select(cl => labels[cl.LabelId])
                    .ToArray()
            })
            .ToArray();

        return JsonSerializer.Serialize(new { cards, total, truncated }, ToolJsonOptions.Default);
    }
}
