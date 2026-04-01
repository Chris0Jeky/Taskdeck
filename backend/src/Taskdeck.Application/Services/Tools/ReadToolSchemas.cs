using System.Text.Json;

namespace Taskdeck.Application.Services.Tools;

/// <summary>
/// Defines the provider-agnostic schemas for all read tools.
/// These schemas are converted to provider-specific wire format by each LLM provider.
/// </summary>
public static class ReadToolSchemas
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static IReadOnlyList<TaskdeckToolSchema> GetAll()
    {
        return new[]
        {
            ListBoardColumns(),
            ListCardsInColumn(),
            GetCardDetails(),
            SearchCards(),
            GetBoardLabels()
        };
    }

    public static TaskdeckToolSchema ListBoardColumns() => new(
        Name: "list_board_columns",
        Description: "List all columns on the current board with their positions and card counts.",
        ParametersSchema: ParseSchema("""
            {
                "type": "object",
                "properties": {},
                "required": [],
                "additionalProperties": false
            }
            """),
        Required: Array.Empty<string>()
    );

    public static TaskdeckToolSchema ListCardsInColumn() => new(
        Name: "list_cards_in_column",
        Description: "List cards in a specific column. Returns card IDs, titles, and labels. Max 20 cards; check 'truncated' field.",
        ParametersSchema: ParseSchema("""
            {
                "type": "object",
                "properties": {
                    "column_name": {
                        "type": "string",
                        "description": "The exact name of the column to list cards from"
                    }
                },
                "required": ["column_name"],
                "additionalProperties": false
            }
            """),
        Required: new[] { "column_name" }
    );

    public static TaskdeckToolSchema GetCardDetails() => new(
        Name: "get_card_details",
        Description: "Get full details of a specific card including description, labels, and dates.",
        ParametersSchema: ParseSchema("""
            {
                "type": "object",
                "properties": {
                    "card_id": {
                        "type": "string",
                        "description": "The 8-character hex ID of the card"
                    }
                },
                "required": ["card_id"],
                "additionalProperties": false
            }
            """),
        Required: new[] { "card_id" }
    );

    public static TaskdeckToolSchema SearchCards() => new(
        Name: "search_cards",
        Description: "Search for cards by title or description text. Returns matching cards with IDs, titles, columns, and labels. Max 15 results.",
        ParametersSchema: ParseSchema("""
            {
                "type": "object",
                "properties": {
                    "query": {
                        "type": "string",
                        "description": "Search text to match against card titles and descriptions"
                    }
                },
                "required": ["query"],
                "additionalProperties": false
            }
            """),
        Required: new[] { "query" }
    );

    public static TaskdeckToolSchema GetBoardLabels() => new(
        Name: "get_board_labels",
        Description: "List all labels available on the current board.",
        ParametersSchema: ParseSchema("""
            {
                "type": "object",
                "properties": {},
                "required": [],
                "additionalProperties": false
            }
            """),
        Required: Array.Empty<string>()
    );

    private static JsonElement ParseSchema(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
