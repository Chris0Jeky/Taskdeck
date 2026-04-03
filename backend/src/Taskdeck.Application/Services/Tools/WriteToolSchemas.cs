using System.Text.Json;

namespace Taskdeck.Application.Services.Tools;

/// <summary>
/// Defines the provider-agnostic schemas for all write tools.
/// Write tools always produce proposals (GP-06 review-first compliance).
/// These schemas are converted to provider-specific wire format by each LLM provider.
/// </summary>
public static class WriteToolSchemas
{
    public static IReadOnlyList<TaskdeckToolSchema> GetAll()
    {
        return new[]
        {
            ProposeCreateCard(),
            ProposeMoveCard(),
            ProposeArchiveCard(),
            ProposeUpdateCard(),
            ProposeBulkMove(),
            ProposeCreateColumn()
        };
    }

    public static TaskdeckToolSchema ProposeCreateCard() => new(
        Name: "propose_create_card",
        Description: "Create a proposal to add a new card to the board. The proposal must be reviewed before it takes effect.",
        ParametersSchema: ParseSchema("""
            {
                "type": "object",
                "properties": {
                    "title": {
                        "type": "string",
                        "description": "The title for the new card"
                    },
                    "column_name": {
                        "type": "string",
                        "description": "Column to place the card in (defaults to first column if omitted)"
                    },
                    "description": {
                        "type": "string",
                        "description": "Optional card description"
                    },
                    "labels": {
                        "type": "array",
                        "items": { "type": "string" },
                        "description": "Optional label names to apply"
                    }
                },
                "required": ["title"],
                "additionalProperties": false
            }
            """),
        Required: new[] { "title" }
    );

    public static TaskdeckToolSchema ProposeMoveCard() => new(
        Name: "propose_move_card",
        Description: "Create a proposal to move a card to a different column. The proposal must be reviewed before it takes effect.",
        ParametersSchema: ParseSchema("""
            {
                "type": "object",
                "properties": {
                    "card_id": {
                        "type": "string",
                        "description": "The 8-character hex ID of the card to move"
                    },
                    "target_column": {
                        "type": "string",
                        "description": "The name of the destination column"
                    }
                },
                "required": ["card_id", "target_column"],
                "additionalProperties": false
            }
            """),
        Required: new[] { "card_id", "target_column" }
    );

    public static TaskdeckToolSchema ProposeArchiveCard() => new(
        Name: "propose_archive_card",
        Description: "Create a proposal to archive a card. The proposal must be reviewed before it takes effect.",
        ParametersSchema: ParseSchema("""
            {
                "type": "object",
                "properties": {
                    "card_id": {
                        "type": "string",
                        "description": "The 8-character hex ID of the card to archive"
                    }
                },
                "required": ["card_id"],
                "additionalProperties": false
            }
            """),
        Required: new[] { "card_id" }
    );

    public static TaskdeckToolSchema ProposeUpdateCard() => new(
        Name: "propose_update_card",
        Description: "Create a proposal to update a card's title, description, or labels. The proposal must be reviewed before it takes effect.",
        ParametersSchema: ParseSchema("""
            {
                "type": "object",
                "properties": {
                    "card_id": {
                        "type": "string",
                        "description": "The 8-character hex ID of the card to update"
                    },
                    "title": {
                        "type": "string",
                        "description": "New title (omit to keep current)"
                    },
                    "description": {
                        "type": "string",
                        "description": "New description (omit to keep current)"
                    },
                    "labels": {
                        "type": "array",
                        "items": { "type": "string" },
                        "description": "New label set (replaces existing labels; omit to keep current)"
                    }
                },
                "required": ["card_id"],
                "additionalProperties": false
            }
            """),
        Required: new[] { "card_id" }
    );

    public static TaskdeckToolSchema ProposeBulkMove() => new(
        Name: "propose_bulk_move",
        Description: "Create a proposal to move multiple cards between columns. Max 50 cards. The proposal must be reviewed before it takes effect.",
        ParametersSchema: ParseSchema("""
            {
                "type": "object",
                "properties": {
                    "source_column": {
                        "type": "string",
                        "description": "Column to move cards from"
                    },
                    "target_column": {
                        "type": "string",
                        "description": "Column to move cards to"
                    },
                    "card_ids": {
                        "type": "array",
                        "items": { "type": "string" },
                        "description": "Specific card IDs to move (omit to move all cards in source column, max 50)"
                    }
                },
                "required": ["source_column", "target_column"],
                "additionalProperties": false
            }
            """),
        Required: new[] { "source_column", "target_column" }
    );

    public static TaskdeckToolSchema ProposeCreateColumn() => new(
        Name: "propose_create_column",
        Description: "Create a proposal to add a new column to the board. The proposal must be reviewed before it takes effect.",
        ParametersSchema: ParseSchema("""
            {
                "type": "object",
                "properties": {
                    "name": {
                        "type": "string",
                        "description": "Name for the new column"
                    },
                    "position": {
                        "type": "integer",
                        "description": "Position index (0-based; omit to append at end)"
                    }
                },
                "required": ["name"],
                "additionalProperties": false
            }
            """),
        Required: new[] { "name" }
    );

    private static JsonElement ParseSchema(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
