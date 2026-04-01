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
    private readonly IUserContextProvider _userContext;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public BoardResources(BoardService boardService, IUserContextProvider userContext)
    {
        _boardService = boardService;
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
            // Phase 1: BoardDto does not carry column/card counts, so we fetch
            // board detail for each board. Acceptable for local SQLite with
            // typically < 20 boards per user. Optimise with a dedicated summary
            // query or DTO enrichment when this becomes a bottleneck.
            var detailResult = await _boardService.GetBoardDetailAsync(boardDto.Id, userId);
            if (!detailResult.IsSuccess)
                continue; // skip boards we can't read (race condition / deleted mid-request)

            var detail = detailResult.Value;

            // Guard against a board archived between the list and detail fetches.
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
}
