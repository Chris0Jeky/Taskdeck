using System.Text.Json;
using Taskdeck.Application.Interfaces;

namespace Taskdeck.Application.Services.Tools;

/// <summary>
/// Executes the search_cards tool: searches for cards by title/description text
/// within the current board. Max 15 results.
/// </summary>
public sealed class SearchCardsExecutor : IToolExecutor
{
    private const int MaxResults = 15;

    private readonly IUnitOfWork _unitOfWork;

    public SearchCardsExecutor(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public string ToolName => "search_cards";

    public async Task<string> ExecuteAsync(Guid boardId, JsonElement arguments, CancellationToken ct = default)
    {
        var query = arguments.TryGetProperty("query", out var q)
            ? q.GetString() ?? ""
            : "";

        if (string.IsNullOrWhiteSpace(query))
        {
            return JsonSerializer.Serialize(new
            {
                error = "query is required",
                suggestion = "Provide a search term to find cards"
            }, ToolJsonOptions.Default);
        }

        var matchingCards = (await _unitOfWork.Cards.SearchAsync(boardId, query, null, null, ct))
            .Take(MaxResults)
            .ToList();

        // Look up column and label names
        var columns = (await _unitOfWork.Columns.GetByBoardIdAsync(boardId, ct))
            .ToDictionary(c => c.Id, c => c.Name);
        var labels = (await _unitOfWork.Labels.GetByBoardIdAsync(boardId, ct))
            .ToDictionary(l => l.Id, l => l.Name);

        var results = matchingCards.Select(c => new
        {
            id = BoardContextBuilder.FormatShortId(c.Id),
            title = c.Title,
            column = columns.GetValueOrDefault(c.ColumnId, "Unknown"),
            labels = c.CardLabels
                .Where(cl => labels.ContainsKey(cl.LabelId))
                .Select(cl => labels[cl.LabelId])
                .ToArray()
        }).ToArray();

        // Get a rough total count (the search repository may return more than MaxResults)
        var allMatching = await _unitOfWork.Cards.SearchAsync(boardId, query, null, null, ct);
        var total = allMatching.Count();

        return JsonSerializer.Serialize(new { results, total }, ToolJsonOptions.Default);
    }
}
