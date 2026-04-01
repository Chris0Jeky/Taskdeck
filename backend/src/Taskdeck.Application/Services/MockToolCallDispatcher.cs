using System.Text.Json;
using System.Text.RegularExpressions;

namespace Taskdeck.Application.Services;

/// <summary>
/// Pattern-matching dispatch table for the Mock LLM provider's tool-calling simulation.
/// Examines the user message and deterministically decides which tool(s) to call.
/// </summary>
public static class MockToolCallDispatcher
{
    private static readonly (Regex Pattern, string ToolName, Func<Match, JsonElement> ArgBuilder)[] Patterns =
    {
        (new Regex(@"(?:what\s+)?cards?\s+(?:are\s+)?in\s+(?:my\s+)?(?:the\s+)?(\w[\w\s]*?)(?:\s+column)?(?:\s*\?|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "list_cards_in_column",
         m => BuildArgs(new { column_name = m.Groups[1].Value.Trim() })),

        (new Regex(@"(?:list|show|get)\s+(?:all\s+)?(?:the\s+)?columns?", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "list_board_columns",
         _ => BuildArgs(new { })),

        (new Regex(@"(?:what|which)\s+columns?\s+(?:do\s+I\s+have|are\s+there|exist)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "list_board_columns",
         _ => BuildArgs(new { })),

        (new Regex(@"(?:details?\s+(?:of|for|about)\s+(?:card\s+)?|card\s+details?\s+(?:for\s+)?)([a-f0-9]{8})", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "get_card_details",
         m => BuildArgs(new { card_id = m.Groups[1].Value })),

        (new Regex(@"search\s+(?:for\s+)?(?:cards?\s+)?(?:matching\s+)?(.+?)(?:\s*\?|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "search_cards",
         m => BuildArgs(new { query = m.Groups[1].Value.Trim() })),

        (new Regex(@"(?:find|look\s+(?:for|up))\s+(.+?)(?:\s*\?|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "search_cards",
         m => BuildArgs(new { query = m.Groups[1].Value.Trim() })),

        (new Regex(@"(?:list|show|get)\s+(?:all\s+)?(?:the\s+)?labels?", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "get_board_labels",
         _ => BuildArgs(new { })),

        (new Regex(@"(?:what|which)\s+labels?\s+(?:do\s+I\s+have|are\s+there|exist)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "get_board_labels",
         _ => BuildArgs(new { })),
    };

    /// <summary>
    /// Attempts to match the user message against known patterns and returns a tool call
    /// request if a match is found. Returns null if no pattern matches.
    /// </summary>
    public static ToolCallRequest? TryDispatch(string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            return null;

        foreach (var (pattern, toolName, argBuilder) in Patterns)
        {
            var match = pattern.Match(userMessage);
            if (match.Success)
            {
                return new ToolCallRequest(
                    CallId: $"mock-call-{Guid.NewGuid():N}"[..16],
                    ToolName: toolName,
                    Arguments: argBuilder(match));
            }
        }

        return null;
    }

    private static JsonElement BuildArgs(object args)
    {
        var json = JsonSerializer.Serialize(args, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}

/// <summary>
/// Provides deterministic mock results for tool executions in test/demo mode.
/// </summary>
public static class MockToolResults
{
    public static string ListBoardColumns() => JsonSerializer.Serialize(new
    {
        columns = new[]
        {
            new { id = "col-0001", name = "Backlog", position = 0, card_count = 5 },
            new { id = "col-0002", name = "In Progress", position = 1, card_count = 3 },
            new { id = "col-0003", name = "Done", position = 2, card_count = 7 }
        }
    });

    public static string ListCardsInColumn(string columnName) => JsonSerializer.Serialize(new
    {
        cards = new[]
        {
            new { id = "a1b2c3d4", title = $"Sample card 1 in {columnName}", labels = new[] { "bug" } },
            new { id = "e5f6a7b8", title = $"Sample card 2 in {columnName}", labels = new[] { "feature" } },
            new { id = "c9d0e1f2", title = $"Sample card 3 in {columnName}", labels = Array.Empty<string>() }
        },
        total = 3,
        truncated = false
    });

    public static string GetCardDetails(string cardId) => JsonSerializer.Serialize(new
    {
        id = cardId,
        title = $"Card {cardId}",
        description = "This is a sample card description for testing.",
        column = "Backlog",
        labels = new[] { "bug" },
        created_at = "2026-03-01T10:00:00Z",
        updated_at = "2026-03-28T15:30:00Z"
    });

    public static string SearchCards(string query) => JsonSerializer.Serialize(new
    {
        results = new[]
        {
            new { id = "a1b2c3d4", title = $"Result for '{query}' (1)", column = "Backlog", labels = new[] { "bug" } },
            new { id = "e5f6a7b8", title = $"Result for '{query}' (2)", column = "In Progress", labels = new[] { "feature" } }
        },
        total = 2
    });

    public static string GetBoardLabels() => JsonSerializer.Serialize(new
    {
        labels = new[]
        {
            new { id = "lbl-001", name = "bug", color = "#e11d48" },
            new { id = "lbl-002", name = "feature", color = "#2563eb" },
            new { id = "lbl-003", name = "urgent", color = "#ea580c" }
        }
    });

    /// <summary>
    /// Executes a mock tool and returns a deterministic result string.
    /// </summary>
    public static string Execute(string toolName, JsonElement arguments)
    {
        return toolName switch
        {
            "list_board_columns" => ListBoardColumns(),
            "list_cards_in_column" => ListCardsInColumn(
                arguments.TryGetProperty("column_name", out var cn) ? cn.GetString() ?? "Unknown" : "Unknown"),
            "get_card_details" => GetCardDetails(
                arguments.TryGetProperty("card_id", out var ci) ? ci.GetString() ?? "00000000" : "00000000"),
            "search_cards" => SearchCards(
                arguments.TryGetProperty("query", out var q) ? q.GetString() ?? "" : ""),
            "get_board_labels" => GetBoardLabels(),
            _ => JsonSerializer.Serialize(new { error = $"Unknown tool: {toolName}" })
        };
    }
}
