using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Mcp;

/// <summary>
/// MCP read-only tools. These execute directly (no proposals) because they
/// do not mutate board state.
/// </summary>
[McpServerToolType]
public class ReadTools
{
    private readonly BoardService _boardService;
    private readonly CardService _cardService;
    private readonly IUserContextProvider _userContext;

    public ReadTools(
        BoardService boardService,
        CardService cardService,
        IUserContextProvider userContext)
    {
        _boardService = boardService;
        _cardService = cardService;
        _userContext = userContext;
    }

    /// <summary>
    /// Search for cards across all accessible boards. Returns matching cards with
    /// board and column context.
    /// </summary>
    [McpServerTool(Name = "search_cards"), Description(
        "Search for cards across all accessible boards. Returns matching cards with board and column context.")]
    public async Task<string> SearchCards(
        [Description("Search text to match against card titles and descriptions")]
        string query,
        [Description("Optional. Restrict search to a specific board (UUID).")]
        string? board_id = null,
        [Description("Maximum results to return. Default 20, max 50.")]
        int max_results = 20)
    {
        var userId = await _userContext.GetCurrentUserIdAsync();

        max_results = Math.Clamp(max_results, 1, 50);

        // If board_id is specified, search that board only
        if (!string.IsNullOrWhiteSpace(board_id))
        {
            if (!Guid.TryParse(board_id, out var boardGuid))
                return JsonSerializer.Serialize(new { error = "Invalid board_id format" }, BoardResources.SerializerOptions);

            var boardResult = await _boardService.GetBoardDetailAsync(boardGuid, userId);
            if (!boardResult.IsSuccess)
                return JsonSerializer.Serialize(new { error = boardResult.ErrorMessage }, BoardResources.SerializerOptions);

            var cardsResult = await _cardService.SearchCardsAsync(boardGuid, searchText: query);
            if (!cardsResult.IsSuccess)
                return JsonSerializer.Serialize(new { error = cardsResult.ErrorMessage }, BoardResources.SerializerOptions);

            var cards = cardsResult.Value.Take(max_results).Select(c => MapCard(c, boardResult.Value.Name, boardResult.Value.Columns));
            return JsonSerializer.Serialize(new { cards, totalCount = cardsResult.Value.Count() }, BoardResources.SerializerOptions);
        }

        // Search across all accessible boards
        var boardsResult = await _boardService.ListBoardsAsync(userId, searchText: null, includeArchived: false);
        if (!boardsResult.IsSuccess)
            return JsonSerializer.Serialize(new { error = boardsResult.ErrorMessage }, BoardResources.SerializerOptions);

        var allCards = new List<object>();
        foreach (var board in boardsResult.Value)
        {
            if (allCards.Count >= max_results) break;

            var detailResult = await _boardService.GetBoardDetailAsync(board.Id, userId);
            if (!detailResult.IsSuccess) continue;

            var searchResult = await _cardService.SearchCardsAsync(board.Id, searchText: query);
            if (!searchResult.IsSuccess) continue;

            var remaining = max_results - allCards.Count;
            allCards.AddRange(searchResult.Value.Take(remaining).Select(c => MapCard(c, board.Name, detailResult.Value.Columns)));
        }

        return JsonSerializer.Serialize(new { cards = allCards, totalCount = allCards.Count }, BoardResources.SerializerOptions);
    }

    /// <summary>
    /// Get a high-level summary of a board: cards per column, total card count,
    /// label distribution, and column overview.
    /// </summary>
    [McpServerTool(Name = "get_board_summary"), Description(
        "Get a high-level summary of a board: cards per column, total card count, label distribution, and recent activity.")]
    public async Task<string> GetBoardSummary(
        [Description("The board ID (UUID)")]
        string board_id)
    {
        var userId = await _userContext.GetCurrentUserIdAsync();

        if (!Guid.TryParse(board_id, out var boardGuid))
            return JsonSerializer.Serialize(new { error = "Invalid board_id format" }, BoardResources.SerializerOptions);

        var detailResult = await _boardService.GetBoardDetailAsync(boardGuid, userId);
        if (!detailResult.IsSuccess)
            return JsonSerializer.Serialize(new { error = detailResult.ErrorMessage }, BoardResources.SerializerOptions);

        var detail = detailResult.Value;

        var columns = detail.Columns.OrderBy(c => c.Position).Select(c => new
        {
            id = c.Id,
            name = c.Name,
            position = c.Position,
            cardCount = c.CardCount,
            wipLimit = c.WipLimit
        });

        var totalCardCount = detail.Columns.Sum(c => c.CardCount);

        return JsonSerializer.Serialize(new
        {
            id = detail.Id,
            name = detail.Name,
            totalCardCount,
            columnCount = detail.Columns.Count,
            columns,
            updatedAt = detail.UpdatedAt
        }, BoardResources.SerializerOptions);
    }

    private static object MapCard(
        Application.DTOs.CardDto card,
        string boardName,
        IReadOnlyList<Application.DTOs.ColumnDto> columns)
    {
        var columnName = columns.FirstOrDefault(c => c.Id == card.ColumnId)?.Name;
        return new
        {
            id = card.Id,
            boardId = card.BoardId,
            boardName,
            columnId = card.ColumnId,
            columnName,
            title = card.Title,
            hasDescription = !string.IsNullOrWhiteSpace(card.Description),
            labels = card.Labels.Select(l => l.Name),
            position = card.Position,
            createdAt = card.CreatedAt
        };
    }
}
