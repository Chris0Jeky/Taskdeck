# Scenarios (JSON Runner)

Taskdeck includes a JSON scenario runner for deterministic demo and test setup.
Use it to seed boards, cards, captures, queue requests, and proposals without manual UI clicking.

Runner files:

- `frontend/taskdeck-web/scripts/scenario-json-runner.mjs`
- `frontend/taskdeck-web/scripts/scenarios-json/*.json`
- `frontend/taskdeck-web/scripts/scenarios-json/schema.v1.json`

## Running scenarios

From `frontend/taskdeck-web`:

```bash
# list available scenarios
npm run demo:run -- --list

# run a scenario
npm run demo:run -- engineering-sprint
npm run demo:run -- support-triage
npm run demo:run -- content-calendar

# skip LLM-required steps
npm run demo:run -- support-triage --skip-llm

# continue after a failed step
npm run demo:run -- engineering-sprint --continue-on-error

# archive DEMO:* boards first
npm run demo:run -- engineering-sprint --clean
```

CI note:

- `--skip-llm` and `--continue-on-error` are for the JSON-runner flow.
- Mark model-dependent steps with `requiresLlm: true` so they can be skipped deterministically.
- `npm run demo:director:smoke` uses the same policy: deterministic seed, no autopilot turns, LLM-required steps skipped, isolated `taskdeck.demo.ci.db`, and forced fresh Playwright servers.

Environment overrides:

- `TASKDECK_API_BASE_URL` (default: `http://localhost:5000/api`)
- `TASKDECK_UI_BASE_URL` (default: `http://localhost:5173`)
- Local fallback UI ports also include `http://localhost:4173` and `http://localhost:5001`.
- Demo scripts reject non-local API targets unless `TASKDECK_DEMO_ALLOW_NON_LOCAL_API=1` is set.

## Template interpolation

Any string field can reference previously created aliases via `${...}` interpolation.

Example:

```json
{
  "type": "queueInstruction",
  "board": "board",
  "instruction": "move card ${cards.designEmptyState.id} to column \"Scheduled\""
}
```

Supported namespaces:

- `boards.<alias>.*`
- `cards.<alias>.*`
- `captures.<alias>.*`
- `proposals.<alias>.*`
- `queueRequests.<alias>.*`
- `opsRuns.<alias>.*`

If interpolation fails to resolve, the value becomes an empty string.

## Step types

### createBoard

Creates a board and stores it under an alias.

```json
{ "type": "createBoard", "alias": "board", "name": "DEMO: X", "description": "..." }
```

### applyStarterPack

Applies a starter pack to an existing board alias.

```json
{ "type": "applyStarterPack", "board": "board", "starterPackId": "board-blueprint-engineering-sprint" }
```

### createCard

Creates a card in a column. Labels and due date are optional.

```json
{
  "type": "createCard",
  "alias": "c1",
  "board": "board",
  "column": "Backlog",
  "title": "Fix bug",
  "description": "repro...",
  "dueInDays": 2,
  "labels": ["bug", "priority-high"]
}
```

### updateCard

Patches a card using the Cards PATCH contract.

```json
{
  "type": "updateCard",
  "board": "board",
  "card": "c1",
  "patch": { "isBlocked": true, "blockReason": "Waiting on X" }
}
```

### moveCard

Moves a card to another column directly via API.

```json
{ "type": "moveCard", "board": "board", "card": "c1", "toColumn": "Done" }
```

### addComment

Adds a comment to a card.

```json
{ "type": "addComment", "board": "board", "card": "c1", "content": "LGTM @collab" }
```

### queueInstruction

Creates a queue request, waits for a proposal, then approves and executes it.

```json
{
  "type": "queueInstruction",
  "board": "board",
  "instruction": "create card \"From scenario\" in column \"Backlog\"",
  "requestAlias": "q1",
  "proposalAlias": "p1"
}
```

### createCapture, triageCapture, waitForCaptureProposal, executeProposal

Capture-loop steps. Triage/proposal execution usually require a live LLM provider.

```json
{ "type": "createCapture", "alias": "cap1", "board": "board", "text": "Customer says checkout fails..." }
{ "type": "triageCapture", "capture": "cap1", "requiresLlm": true }
{ "type": "waitForCaptureProposal", "capture": "cap1", "proposalAlias": "cap1Proposal", "requiresLlm": true }
{ "type": "executeProposal", "proposal": "cap1Proposal", "requiresLlm": true }
```

Use `--skip-llm` to skip steps marked with `requiresLlm: true`.

### runOps

Runs an Ops template and optionally waits for completion (default) and fetches logs.

```json
{
  "type": "runOps",
  "templateName": "health.check",
  "parameters": {},
  "includeLogs": true,
  "alias": "opsHealth"
}
```

Optional fields:
- `wait` (default `true`): set `false` to return immediately after enqueueing.
- `timeoutMs`, `intervalMs`: poll controls when waiting.
- `parameters`: optional object; all values must be strings to match the Ops API contract.

## Extending the runner

1. Update `schema.v1.json` (recommended).
2. Add a `case` in `executeStep()` inside `scenario-json-runner.mjs`.
3. Add a minimal scenario file in `scripts/scenarios-json/`.

Keep step semantics deterministic if you want to reuse scenarios in tests.
For LLM-driven steps, set `requiresLlm: true` so they can be skipped in CI.
