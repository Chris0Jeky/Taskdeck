using System.Text.Json;
using Taskdeck.Application.Interfaces;

namespace Taskdeck.Application.Services.Tools;

/// <summary>
/// Executes the list_board_columns tool: returns all columns on the board
/// with their positions and card counts.
/// </summary>
public sealed class ListBoardColumnsExecutor : IToolExecutor
{
    private readonly IUnitOfWork _unitOfWork;

    public ListBoardColumnsExecutor(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public string ToolName => "list_board_columns";

    public async Task<string> ExecuteAsync(Guid boardId, JsonElement arguments, CancellationToken ct = default)
    {
        var columns = (await _unitOfWork.Columns.GetByBoardIdAsync(boardId, ct))
            .OrderBy(c => c.Position)
            .ToList();

        // Get card counts per column
        var allCards = await _unitOfWork.Cards.GetByBoardIdAsync(boardId, ct);
        var cardCounts = allCards
            .GroupBy(c => c.ColumnId)
            .ToDictionary(g => g.Key, g => g.Count());

        var result = new
        {
            columns = columns.Select(c => new
            {
                id = BoardContextBuilder.FormatShortId(c.Id),
                name = c.Name,
                position = c.Position,
                card_count = cardCounts.GetValueOrDefault(c.Id, 0)
            }).ToArray()
        };

        return JsonSerializer.Serialize(result, ToolJsonOptions.Default);
    }
}
