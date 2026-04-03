using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Mcp;

/// <summary>
/// MCP resource provider for Taskdeck boards.
/// Exposes board data as read-only MCP resources with compact JSON optimised for
/// LLM context windows (summary by default, no deep nesting).
/// All queries are scoped to the authenticated user via <see cref="IUserContextProvider"/>.
/// </summary>
[McpServerResourceType]
public class BoardResources
{
    private readonly BoardService _boardService;
    private readonly ColumnService _columnService;
    private readonly CardService _cardService;
    private readonly LabelService _labelService;
    private readonly IUserContextProvider _userContext;

    internal static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public BoardResources(
        BoardService boardService,
        ColumnService columnService,
        CardService cardService,
        LabelService labelService,
        IUserContextProvider userContext)
    {
        _boardService = boardService;
        _columnService = columnService;
        _cardService = cardService;
        _labelService = labelService;
        _userContext = userContext;
    }

    /// <summary>
    /// Lists all active (non-archived) boards accessible to the current user.
    /// Returns compact JSON optimised for LLM context windows.
    /// Shape: { "boards": [{ "id", "name", "columnCount", "cardCount", "isArchived", "updatedAt" }], "totalCount" }
    /// </summary>
    [McpServerResource(
        UriTemplate = "taskdeck://boards",
        Name = "boards",
        Title = "All Boards",
        MimeType = "application/json")]
    public async Task<string> ListBoards()
    {
        var userId = await _userContext.GetCurrentUserIdAsync();

        // List board summaries scoped to this user.
        var listResult = await _boardService.ListBoardsAsync(userId, searchText: null, includeArchived: false);
        if (!listResult.IsSuccess)
            throw new InvalidOperationException($"MCP: failed to list boards: {listResult.ErrorMessage}");

        var boardSummaries = new List<object>();
        foreach (var boardDto in listResult.Value)
        {
            var detailResult = await _boardService.GetBoardDetailAsync(boardDto.Id, userId);
            if (!detailResult.IsSuccess)
                continue;

            var detail = detailResult.Value;

            if (detail.IsArchived)
                continue;

            var cardCount = detail.Columns.Sum(c => c.CardCount);

            boardSummaries.Add(new
            {
                id = detail.Id,
                name = detail.Name,
                columnCount = detail.Columns.Count,
                cardCount,
                isArchived = detail.IsArchived,
                updatedAt = detail.UpdatedAt
            });
        }

        return JsonSerializer.Serialize(
            new { boards = boardSummaries, totalCount = boardSummaries.Count },
            SerializerOptions);
    }

    /// <summary>
    /// Returns board detail with columns, labels, and card counts.
    /// </summary>
    [McpServerResource(
        UriTemplate = "taskdeck://boards/{boardId}",
        Name = "board_detail",
        Title = "Board Detail",
        MimeType = "application/json")]
    public async Task<string> GetBoardDetail(string boardId)
    {
        var userId = await _userContext.GetCurrentUserIdAsync();

        if (!Guid.TryParse(boardId, out var boardGuid))
            throw new ArgumentException($"MCP: invalid board ID '{boardId}'");

        var detailResult = await _boardService.GetBoardDetailAsync(boardGuid, userId);
        if (!detailResult.IsSuccess)
            throw new InvalidOperationException($"MCP: failed to get board detail: {detailResult.ErrorMessage}");

        var detail = detailResult.Value;

        var labelsResult = await _labelService.GetLabelsByBoardIdAsync(boardGuid);
        var labels = labelsResult.IsSuccess
            ? labelsResult.Value.Select(l => new { id = l.Id, name = l.Name, color = l.ColorHex })
            : Enumerable.Empty<object>();

        var columns = detail.Columns.Select(c => new
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
            columns,
            labels,
            cardCount = totalCardCount,
            updatedAt = detail.UpdatedAt
        }, SerializerOptions);
    }

    /// <summary>
    /// Returns cards in a specific column with compact summaries.
    /// </summary>
    [McpServerResource(
        UriTemplate = "taskdeck://boards/{boardId}/columns/{columnId}/cards",
        Name = "column_cards",
        Title = "Cards in Column",
        MimeType = "application/json")]
    public async Task<string> GetColumnCards(string boardId, string columnId)
    {
        var userId = await _userContext.GetCurrentUserIdAsync();

        if (!Guid.TryParse(boardId, out var boardGuid))
            throw new ArgumentException($"MCP: invalid board ID '{boardId}'");
        if (!Guid.TryParse(columnId, out var columnGuid))
            throw new ArgumentException($"MCP: invalid column ID '{columnId}'");

        // Verify user can access this board
        var boardResult = await _boardService.GetBoardDetailAsync(boardGuid, userId);
        if (!boardResult.IsSuccess)
            throw new InvalidOperationException($"MCP: failed to access board: {boardResult.ErrorMessage}");

        var column = boardResult.Value.Columns.FirstOrDefault(c => c.Id == columnGuid);
        if (column == null)
            throw new InvalidOperationException($"MCP: column {columnId} not found in board {boardId}");

        var cardsResult = await _cardService.SearchCardsAsync(boardGuid, columnId: columnGuid);
        if (!cardsResult.IsSuccess)
            throw new InvalidOperationException($"MCP: failed to get cards: {cardsResult.ErrorMessage}");

        var cards = cardsResult.Value.OrderBy(c => c.Position).Select(c => new
        {
            id = c.Id,
            title = c.Title,
            position = c.Position,
            labels = c.Labels.Select(l => l.Name),
            hasDescription = !string.IsNullOrWhiteSpace(c.Description),
            createdAt = c.CreatedAt
        });

        return JsonSerializer.Serialize(new
        {
            columnId = columnGuid,
            columnName = column.Name,
            cards,
            totalCount = cardsResult.Value.Count()
        }, SerializerOptions);
    }

    /// <summary>
    /// Returns full card detail including labels, description, and provenance.
    /// </summary>
    [McpServerResource(
        UriTemplate = "taskdeck://boards/{boardId}/cards/{cardId}",
        Name = "card_detail",
        Title = "Card Detail",
        MimeType = "application/json")]
    public async Task<string> GetCardDetail(string boardId, string cardId)
    {
        var userId = await _userContext.GetCurrentUserIdAsync();

        if (!Guid.TryParse(boardId, out var boardGuid))
            throw new ArgumentException($"MCP: invalid board ID '{boardId}'");
        if (!Guid.TryParse(cardId, out var cardGuid))
            throw new ArgumentException($"MCP: invalid card ID '{cardId}'");

        // Verify user can access this board
        var boardResult = await _boardService.GetBoardDetailAsync(boardGuid, userId);
        if (!boardResult.IsSuccess)
            throw new InvalidOperationException($"MCP: failed to access board: {boardResult.ErrorMessage}");

        var cardsResult = await _cardService.SearchCardsAsync(boardGuid);
        if (!cardsResult.IsSuccess)
            throw new InvalidOperationException($"MCP: failed to search cards: {cardsResult.ErrorMessage}");

        var card = cardsResult.Value.FirstOrDefault(c => c.Id == cardGuid);
        if (card == null)
            throw new InvalidOperationException($"MCP: card {cardId} not found in board {boardId}");

        // Find the column name
        var columnName = boardResult.Value.Columns
            .FirstOrDefault(c => c.Id == card.ColumnId)?.Name;

        return JsonSerializer.Serialize(new
        {
            id = card.Id,
            boardId = card.BoardId,
            columnId = card.ColumnId,
            columnName,
            title = card.Title,
            description = card.Description,
            position = card.Position,
            isBlocked = card.IsBlocked,
            blockReason = card.BlockReason,
            dueDate = card.DueDate,
            labels = card.Labels.Select(l => new { id = l.Id, name = l.Name, color = l.ColorHex }),
            createdAt = card.CreatedAt,
            updatedAt = card.UpdatedAt
        }, SerializerOptions);
    }

    /// <summary>
    /// Returns available labels for a board.
    /// </summary>
    [McpServerResource(
        UriTemplate = "taskdeck://boards/{boardId}/labels",
        Name = "board_labels",
        Title = "Board Labels",
        MimeType = "application/json")]
    public async Task<string> GetBoardLabels(string boardId)
    {
        var userId = await _userContext.GetCurrentUserIdAsync();

        if (!Guid.TryParse(boardId, out var boardGuid))
            throw new ArgumentException($"MCP: invalid board ID '{boardId}'");

        // Verify user can access this board
        var boardResult = await _boardService.GetBoardDetailAsync(boardGuid, userId);
        if (!boardResult.IsSuccess)
            throw new InvalidOperationException($"MCP: failed to access board: {boardResult.ErrorMessage}");

        var labelsResult = await _labelService.GetLabelsByBoardIdAsync(boardGuid);
        if (!labelsResult.IsSuccess)
            throw new InvalidOperationException($"MCP: failed to get labels: {labelsResult.ErrorMessage}");

        var labels = labelsResult.Value.Select(l => new
        {
            id = l.Id,
            name = l.Name,
            color = l.ColorHex
        });

        return JsonSerializer.Serialize(new
        {
            boardId = boardGuid,
            boardName = boardResult.Value.Name,
            labels,
            totalCount = labelsResult.Value.Count()
        }, SerializerOptions);
    }
}
