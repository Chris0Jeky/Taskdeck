# Golden Eval Fixtures

Synthetic test fixtures for evaluating Taskdeck's capture-to-proposal pipeline.
Each fixture defines an input capture, the expected interpretation, and the
expected proposal output (or expected clarification request).

## Fixture Format

Each fixture is a JSON file conforming to `format.schema.json`. Key fields:

| Field | Type | Description |
|-------|------|-------------|
| `id` | string | Unique fixture identifier (e.g. `happy-001`) |
| `category` | enum | One of: `happy-path`, `multi-instruction`, `ambiguous`, `safety-boundary` |
| `description` | string | Human-readable explanation of what this fixture tests |
| `input` | object | The capture input (text, source, optional boardContext) |
| `expected` | object | Expected pipeline output (proposals, clarifications, rejections) |
| `tags` | string[] | Searchable tags for filtering fixtures |

## Categories

- **happy-path**: Clean, unambiguous captures that should produce exactly one proposal.
- **multi-instruction**: Captures containing multiple instructions that should
  produce multiple proposals from a single input.
- **ambiguous**: Captures that need clarification before a proposal can be generated.
  The expected output includes a clarification request.
- **safety-boundary**: Captures that test injection attempts, PII handling, or
  other safety boundaries. The expected output may be a rejection or sanitized proposal.

## Adding New Fixtures

1. Create a JSON file matching `format.schema.json`.
2. Use a descriptive filename: `{category}-{NNN}-{short-description}.json`
3. Include the `description` field explaining what edge case the fixture covers.
4. Run the eval harness (when available) to verify: `npm run eval:golden`

## Board Context

Fixtures that reference board state include a `boardContext` object with columns
and cards. This simulates the board context that would be available to the LLM
during instruction extraction.
