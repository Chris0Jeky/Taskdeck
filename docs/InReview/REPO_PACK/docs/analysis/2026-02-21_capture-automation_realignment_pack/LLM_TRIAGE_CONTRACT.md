# LLM Triage Contract (Structured Output)
Date: 2026-02-21
Status: Draft (analysis pack; non-authoritative)

## Objective
Given a capture artifact (`RawText`), return a **strict JSON payload** that can be validated deterministically and transformed into a proposal diff.

## Principles
- Output must be machine-validated (JSON Schema).
- The model must not decide permissions, identities, or security-sensitive fields.
- Prefer conservative suggestions:
  - if uncertain, ask clarifying questions
  - do not create many new labels/columns unless strongly supported

## Output JSON schema (draft)

### Overview
The model returns:
- `summary`: short intent
- `tasks`: structured candidate tasks
- `suggestedLabels`: optional new labels
- `suggestedColumns`: optional new columns
- `clarifyingQuestions`: optional
- `confidence`: overall numeric confidence
- `safety`: risk hints (always “proposal required” for now)

### Draft JSON Schema (informal)
```json
{
  "summary": "string",
  "confidence": 0.0,
  "clarifyingQuestions": ["string"],
  "tasks": [
    {
      "title": "string",
      "description": "string",
      "labels": ["string"],
      "dueDate": "YYYY-MM-DD|null",
      "blockedReason": "string|null",
      "target": {
        "boardHint": "string|null",
        "columnHint": "string|null"
      },
      "sourceEvidence": [
        { "quote": "string", "startOffset": 0, "endOffset": 10 }
      ],
      "confidence": 0.0
    }
  ],
  "suggestedLabels": [
    { "name": "string", "colorHint": "string|null", "confidence": 0.0 }
  ],
  "suggestedColumns": [
    { "name": "string", "wipLimitHint": 3, "confidence": 0.0 }
  ],
  "safety": {
    "requiresProposal": true,
    "notes": ["string"]
  }
}
```

### Notes on fields
- `labels` in tasks should prefer *existing labels* when possible; backend will resolve.
- `target.boardHint` should be null unless user explicitly references a board; MVP can assume “current board” context in UI.
- `sourceEvidence` is critical for trust; UI should be able to show where the task came from.

## JSON Schema (Draft 2020-12) — explicit
Below is a strict schema you can embed in code and tests.

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://taskdeck.dev/schemas/triage-output.v1.json",
  "type": "object",
  "additionalProperties": false,
  "required": ["summary","confidence","tasks","clarifyingQuestions","suggestedLabels","suggestedColumns","safety"],
  "properties": {
    "summary": { "type": "string", "minLength": 1, "maxLength": 240 },
    "confidence": { "type": "number", "minimum": 0, "maximum": 1 },
    "clarifyingQuestions": {
      "type": "array",
      "items": { "type": "string", "minLength": 1, "maxLength": 240 },
      "maxItems": 5
    },
    "tasks": {
      "type": "array",
      "maxItems": 20,
      "items": {
        "type": "object",
        "additionalProperties": false,
        "required": ["title","description","labels","dueDate","blockedReason","target","sourceEvidence","confidence"],
        "properties": {
          "title": { "type": "string", "minLength": 1, "maxLength": 160 },
          "description": { "type": "string", "maxLength": 4000 },
          "labels": {
            "type": "array",
            "items": { "type": "string", "minLength": 1, "maxLength": 60 },
            "maxItems": 8
          },
          "dueDate": {
            "oneOf": [
              { "type": "null" },
              { "type": "string", "pattern": "^\d{4}-\d{2}-\d{2}$" }
            ]
          },
          "blockedReason": {
            "oneOf": [
              { "type": "null" },
              { "type": "string", "minLength": 1, "maxLength": 240 }
            ]
          },
          "target": {
            "type": "object",
            "additionalProperties": false,
            "required": ["boardHint","columnHint"],
            "properties": {
              "boardHint": { "type": ["string","null"], "maxLength": 120 },
              "columnHint": { "type": ["string","null"], "maxLength": 120 }
            }
          },
          "sourceEvidence": {
            "type": "array",
            "minItems": 1,
            "maxItems": 3,
            "items": {
              "type": "object",
              "additionalProperties": false,
              "required": ["quote","startOffset","endOffset"],
              "properties": {
                "quote": { "type": "string", "minLength": 1, "maxLength": 240 },
                "startOffset": { "type": "integer", "minimum": 0 },
                "endOffset": { "type": "integer", "minimum": 0 }
              }
            }
          },
          "confidence": { "type": "number", "minimum": 0, "maximum": 1 }
        }
      }
    },
    "suggestedLabels": {
      "type": "array",
      "maxItems": 10,
      "items": {
        "type": "object",
        "additionalProperties": false,
        "required": ["name","colorHint","confidence"],
        "properties": {
          "name": { "type": "string", "minLength": 1, "maxLength": 60 },
          "colorHint": { "type": ["string","null"], "maxLength": 32 },
          "confidence": { "type": "number", "minimum": 0, "maximum": 1 }
        }
      }
    },
    "suggestedColumns": {
      "type": "array",
      "maxItems": 10,
      "items": {
        "type": "object",
        "additionalProperties": false,
        "required": ["name","wipLimitHint","confidence"],
        "properties": {
          "name": { "type": "string", "minLength": 1, "maxLength": 60 },
          "wipLimitHint": { "type": ["integer","null"], "minimum": 1, "maximum": 20 },
          "confidence": { "type": "number", "minimum": 0, "maximum": 1 }
        }
      }
    },
    "safety": {
      "type": "object",
      "additionalProperties": false,
      "required": ["requiresProposal","notes"],
      "properties": {
        "requiresProposal": { "type": "boolean", "const": true },
        "notes": {
          "type": "array",
          "items": { "type": "string", "minLength": 1, "maxLength": 240 },
          "maxItems": 5
        }
      }
    }
  }
}
```

## Prompting strategy (v1)
### System instruction (conceptual)
- You are a task triage assistant.
- Convert raw notes into actionable tasks.
- Output JSON only that matches schema.
- Include evidence quotes and offsets.

### User message format
Provide model input as:
- artifact text
- optionally current board context (existing labels/columns) to reduce hallucinated suggestions

## Deterministic transformation rules (backend)
- Tasks map to operations:
  - ensure label exists (existing → reference; suggested → create label op)
  - create card in Inbox column or chosen column
- Reject output that fails schema or exceeds limits.
- If `clarifyingQuestions` non-empty and `confidence < threshold`, consider returning a proposal with “requires clarification” state (future).

## Testing requirements
- Add “golden” triage fixtures:
  - simple to-do list
  - meeting transcript excerpt
  - ambiguous notes requiring clarifying questions
- Use Mock provider in tests to return deterministic JSON.
- Add negative tests: invalid schema, too many tasks, missing evidence.
