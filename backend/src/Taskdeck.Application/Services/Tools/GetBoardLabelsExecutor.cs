using System.Text.Json;
using Taskdeck.Application.Interfaces;

namespace Taskdeck.Application.Services.Tools;

/// <summary>
/// Executes the get_board_labels tool: returns all labels on the board.
/// </summary>
public sealed class GetBoardLabelsExecutor : IToolExecutor
{
    private readonly IUnitOfWork _unitOfWork;

    public GetBoardLabelsExecutor(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public string ToolName => "get_board_labels";

    public async Task<string> ExecuteAsync(Guid boardId, JsonElement arguments, CancellationToken ct = default)
    {
        var labels = (await _unitOfWork.Labels.GetByBoardIdAsync(boardId, ct))
            .Select(l => new
            {
                id = BoardContextBuilder.FormatShortId(l.Id),
                name = l.Name,
                color = l.ColorHex
            })
            .ToArray();

        return JsonSerializer.Serialize(new { labels }, ToolJsonOptions.Default);
    }
}
