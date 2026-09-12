using System.Text.Json;

namespace Taskdeck.Application.Services.Tools;

/// <summary>
/// Defines the provider-agnostic schemas for all write tools.
/// Write tools always produce proposals (GP-06 review-first compliance).
/// These schemas are converted to provider-specific wire format by each LLM provider.
/// </summary>
public static class WriteToolSchemas
{
    private static readonly IReadOnlyList<TaskdeckToolSchema> CachedAll = new[]
    {
        ProposeCreateCard(),
        ProposeMoveCard(),
        ProposeArchiveCard(),
        ProposeUpdateCard(),
        ProposeBulkMove(),
        ProposeCreateColumn(),
        ProposeAddCardRelation(),
        ProposeRemoveCardRelation()
    };

    public static IReadOnlyList<TaskdeckToolSchema> GetAll() => CachedAll;

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
                    "due_date": {
                        "type": "string",
                        "description": "Optional due date as YYYY-MM-DD or an ISO-8601 timestamp with an offset"
                    },
                    "estimated_effort_minutes": {
                        "type": ["integer", "null"],
                        "minimum": 0,
                        "maximum": 1000000,
                        "description": "Optional planned effort in whole minutes; 0 is known zero and null or omission means unknown. This is not logged time."
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
        Description: "Create a proposal to mark a card blocked. Nothing changes immediately. After explicit review and approval, Apply marks the card blocked with the generated reason 'Archived by an approved proposal.'",
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
        Description: "Create a proposal to update a card's title, description, due date, effort estimate, or labels. The proposal must be reviewed before it takes effect.",
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
                    "due_date": {
                        "type": "string",
                        "description": "New due date as YYYY-MM-DD or an ISO-8601 timestamp with an offset (omit to keep current)"
                    },
                    "clear_due_date": {
                        "type": "boolean",
                        "description": "Set true to remove the current due date; do not combine with due_date"
                    },
                    "estimated_effort_minutes": {
                        "type": ["integer", "null"],
                        "minimum": 0,
                        "maximum": 1000000,
                        "description": "Planned effort in whole minutes; 0 is known zero and null or omission keeps the current estimate. Requires expected_updated_at; do not combine with clear_estimated_effort true."
                    },
                    "clear_estimated_effort": {
                        "type": "boolean",
                        "description": "Set true to clear the estimate to unknown. Requires expected_updated_at; do not combine with a non-null estimated_effort_minutes."
                    },
                    "expected_updated_at": {
                        "type": "string",
                        "description": "The exact updated_at from get_card_details; required when setting or clearing the estimate. Refresh the card when stale."
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

    public static TaskdeckToolSchema ProposeAddCardRelation() => CardRelationSchema(
        "propose_add_card_relation",
        "Create a proposal to add one typed relation between two active cards on this board. The proposal must be reviewed before it takes effect.");

    public static TaskdeckToolSchema ProposeRemoveCardRelation() => CardRelationSchema(
        "propose_remove_card_relation",
        "Create a proposal to remove one typed relation between two active cards on this board. The proposal must be reviewed before it takes effect.");

    private static TaskdeckToolSchema CardRelationSchema(string name, string description) => new(
        Name: name,
        Description: description,
        ParametersSchema: ParseSchema("""
            {
                "type": "object",
                "properties": {
                    "card_id": {
                        "type": "string",
                        "description": "Source card full UUID or an unambiguous short ID from the current board"
                    },
                    "related_card_id": {
                        "type": "string",
                        "description": "Related card full UUID or an unambiguous short ID from the current board"
                    },
                    "relation_type": {
                        "type": "string",
                        "enum": ["relates-to", "blocks", "depends-on", "duplicates", "spawned-from"],
                        "description": "Relation kind. depends-on is canonicalized to blocks with reversed endpoints."
                    },
                    "expected_revision": {
                        "type": "integer",
                        "minimum": 0,
                        "description": "The exact relations revision returned by get_board_card_relations; refresh when stale."
                    }
                },
                "required": ["card_id", "related_card_id", "relation_type", "expected_revision"],
                "additionalProperties": false
            }
            """),
        Required: new[] { "card_id", "related_card_id", "relation_type", "expected_revision" }
    );

    private static JsonElement ParseSchema(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
