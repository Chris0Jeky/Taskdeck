# Taskdeck Product Telemetry Taxonomy

Last Updated: 2026-04-04
Status: Draft

## Purpose

This document defines the canonical event taxonomy for Taskdeck product telemetry. It establishes naming conventions, per-event property contracts, and hard privacy guardrails.

Telemetry is **opt-in and disabled by default**. No events are emitted without explicit user consent. This is non-negotiable given Taskdeck's local-first, privacy-first stance.

Related: #341, #77

---

## Opt-In Stance

- Telemetry collection is gated behind a user preference (`settings.telemetry.enabled`, default: `false`).
- No events leave the device until the user explicitly enables telemetry.
- Events collected locally (for personal analytics dashboards) follow the same property rules as opt-in remote telemetry.
- The backend MUST NOT forward telemetry payloads to any third-party without explicit configuration and opt-in consent.

**Implementation status**: The `settings.telemetry.enabled` preference and the telemetry service/event bus described in this document are **not yet implemented**. This taxonomy is the planning artefact that must precede instrumentation. No telemetry code should be merged until this taxonomy is ratified and the opt-in guard is in place.

---

## Event Naming Convention

Format: `noun.verb`

- **Noun**: the primary product entity or surface (e.g. `card`, `proposal`, `board`, `session`)
- **Verb**: the action or state transition (e.g. `created`, `approved`, `loaded`, `failed`)

Examples:
- `card.created`
- `proposal.approved`
- `auth_session.started`
- `agent_run.completed`

Rules:
- Event names consist of two dot-separated segments: `noun.verb`.
- All segments are lowercase. Use underscores within any multi-word segment for legibility (e.g. `agent_run`, `first_run`, `modal_opened`, `inbox_loaded`).
- Use past-tense or past-participle form for completed actions and resulting state events (e.g. `created`, `approved`, `loaded`, `opened`, `failed`). Do not mix in present-tense verb forms.
- No abbreviations. Prefer legibility.
- New events must be added to this taxonomy before instrumentation is merged.

---

## Universal Envelope Properties

Every event MUST include:

| Property | Type | Description |
|---|---|---|
| `event` | string | Event name (e.g. `card.created`) |
| `timestamp` | ISO 8601 string | UTC timestamp of the event |
| `session_id` | string (UUID) | Anonymous session identifier, rotated on app restart |
| `workspace_mode` | string | Current workspace mode: `guided`, `workbench`, or `agent` |
| `app_version` | string | Semver of the running app |
| `platform` | string | `web`, `desktop`, or `cli` |

No user identity, email, or username is included in the envelope.

---

## Privacy Guardrails

**NEVER collect:**

- Card titles, card descriptions, or any card content
- Column names or board names (treat as user content)
- Proposal text, diff content, or LLM prompt/response bodies
- Usernames, display names, or email addresses
- OAuth tokens, session tokens, or auth credentials
- File paths, system paths, or machine hostnames
- Any free-text field entered by the user

**Safe to collect:**
- Entity IDs (opaque UUIDs only — no human-readable slugs)
- Counts, durations, and boolean flags
- Enumerated values from a fixed set (e.g. workspace mode, provider type, risk level)
- Success/failure/error code (no error message body from user-generated content)
- Feature flag states

When in doubt: **omit the property**. It is always safe to collect less.

---

## Category 1: Capture Events

Events covering the inbox capture and triage flow.

### `capture.modal_opened`
User opened the capture modal.

Required: *(envelope only)*
Optional: `trigger_source: string` — how the modal was opened: `keyboard_shortcut`, `nav_button`, `board_action`

### `capture.submitted`
User submitted a capture item.

Required: `has_attachment: boolean`, `source: string` — submission origin: `manual`, `import`
Optional: `input_length_bucket: string` — bucketed character count: `short` (<50), `medium` (50–200), `long` (>200). Do not collect exact length.

### `capture.cancelled`
User dismissed the capture modal without submitting.

Required: *(envelope only)*

### `capture.triage_clicked`
User clicked the triage action on a capture item in the inbox.

Required: `item_id: string` — opaque UUID only; no content derived from the item

### `capture.inbox_loaded`
Inbox view loaded successfully.

Required: `item_count_bucket: string` — bucketed: `empty`, `small` (1–10), `medium` (11–50), `large` (>50)

### `capture.inbox_load_failed`
Inbox view failed to load.

Required: `error_code: string`

---

## Category 2: Proposal / Review Events

Events covering the review-first automation flow.

### `proposal.list_loaded`
Review/proposals list loaded successfully.

Required: `pending_count_bucket: string` — bucketed: `empty`, `small` (1–10), `medium` (11–50), `large` (>50)

### `proposal.opened`
User opened a proposal detail.

Required: `proposal_id: string`
Optional: `proposal_risk_level: string` — e.g. `low`, `medium`, `high`

### `proposal.approved`
User explicitly approved a proposal.

Required: `proposal_id: string`, `proposal_risk_level: string`
Optional: `time_to_decision_ms: number` — milliseconds from `proposal.opened` to approval

### `proposal.rejected`
User explicitly rejected a proposal.

Required: `proposal_id: string`, `proposal_risk_level: string`
Optional: `time_to_decision_ms: number` — milliseconds from `proposal.opened` to rejection

### `proposal.executed`
A proposal was successfully executed and applied to the board.

Required: `proposal_id: string`, `proposal_type: string` — e.g. `card_create`, `card_move`, `column_add`
Optional: `execution_duration_ms: number`

### `proposal.execution_failed`
Proposal execution failed.

Required: `proposal_id: string`, `error_code: string`

### `proposal.bulk_approved`
User bulk-approved multiple proposals.

Required: `approved_count_bucket: string` — bucketed: `small` (1–5), `medium` (6–20), `large` (>20)

### `proposal.bulk_rejected`
User bulk-rejected multiple proposals.

Required: `rejected_count_bucket: string` — bucketed: `small` (1–5), `medium` (6–20), `large` (>20)

---

## Category 3: Board Events

Events covering board and card lifecycle.

### `board.loaded`
A board view loaded successfully.

Required: `card_count_bucket: string` — `empty`, `small` (1–20), `medium` (21–100), `large` (>100)
Optional: `column_count: number`

### `board.load_failed`
Board failed to load.

Required: `error_code: string`

### `board.created`
User created a new board.

Required: *(envelope only)*

### `board.starter_pack_applied`
User applied a starter pack to a board.

Required: `starter_pack_id: string`

### `card.created`
A card was created (manually or via proposal).

Required: `source: string` — `manual`, `proposal`, `import`

### `card.moved`
A card was moved between columns.

Required: `source: string` — `drag_drop`, `keyboard`, `proposal`

### `card.archived`
A card was archived.

Required: `source: string` — `manual`, `proposal`

### `card.blocked`
A card was marked as blocked.

Required: *(envelope only)*

### `card.unblocked`
A card was unblocked.

Required: *(envelope only)*

### `card.deleted`
A card was deleted.

Required: `source: string` — `manual`, `proposal`

---

## Category 4: Auth Events

Events covering authentication and session lifecycle.

Note: These events use the `auth_session` noun (not `session`) to avoid confusion with the universal envelope's `session_id`, which identifies the anonymous app session — a different concept.

### `auth_session.started`
User started a new authenticated session.

Required: `auth_method: string` — `password`, `oauth_google`, `oauth_github`

### `auth_session.ended`
User explicitly signed out.

Required: *(envelope only)*

### `auth_session.expired`
Auth session expired and user was redirected to login.

Required: *(envelope only)*

### `auth.login_failed`
Login attempt failed.

Required: `status_code: number` — HTTP status code (e.g. `401`, `429`). Do not include credential content.

### `auth.register_completed`
User completed registration.

Required: `auth_method: string`

---

## Category 5: Navigation Events

Events covering page navigation and workspace mode transitions.

### `page.loaded`
A route-level page loaded successfully.

Required: `page: string` — current route-level values: `home`, `today`, `inbox`, `review`, `board`, `metrics`, `settings`

Reserved/future values (do not emit until the corresponding router surface exists and instrumentation is wired): `agents`, `agent_run_detail`, `knowledge`, `help` (tracked via #341, #77)
Optional: `load_duration_ms: number`

### `page.load_failed`
A page failed to load (routing or data error).

Required: `page: string` — use the same value set as `page.loaded`, `error_code: string`

### `workspace_mode.changed`
User changed the workspace mode.

Required: `from_mode: string`, `to_mode: string` — values from `guided`, `workbench`, `agent`

### `first_run.wizard_started`
User started the first-run wizard.

Required: *(envelope only)*

### `first_run.wizard_completed`
User completed the first-run wizard.

Required: `steps_completed: number`

### `first_run.wizard_skipped`
User skipped the first-run wizard.

Required: `step_skipped_at: number` — which step index was active when skipped

### `onboarding.checklist_item_completed`
User completed a checklist item in the onboarding flow.

Required: `item_key: string` — enumerated checklist item identifier, not user-entered text

---

## Category 6: Agent Events

Events covering agent profiles and run lifecycle. Only relevant when agent workspace mode is enabled.

### `agent.created`
User created an agent profile.

Required: *(envelope only)*

### `agent_run.started`
An agent run was initiated.

Required: `agent_id: string`, `trigger: string` — `manual`, `scheduled`, `inbound_webhook`

### `agent_run.completed`
An agent run completed successfully.

Required: `agent_id: string`, `run_id: string`
Optional: `step_count: number`, `duration_ms: number`, `proposals_created_bucket: string` — bucketed: `none` (0), `small` (1–5), `medium` (6–20), `large` (>20)

### `agent_run.failed`
An agent run failed.

Required: `agent_id: string`, `run_id: string`, `error_code: string`
Optional: `step_count: number`, `duration_ms: number`

### `agent_run.cancelled`
An agent run was cancelled by the user.

Required: `agent_id: string`, `run_id: string`

### `agent_run.proposal_linked`
An agent run produced and linked a proposal.

Required: `run_id: string`, `proposal_id: string`, `proposal_risk_level: string`

### `mcp_tool.invoked`
An MCP tool was called (via stdio or HTTP transport). Only relevant when the MCP server is active.

Required: `tool_name: string` — enumerated tool name (e.g. `create_card`, `list_boards`); do not include argument values
Optional: `transport: string` — `stdio`, `http`

### `mcp_tool.failed`
An MCP tool call failed.

Required: `tool_name: string`, `error_code: string`
Optional: `transport: string`

---

## Category 7: Error Events

Events covering product-level errors that affect the user experience.

### `error.unhandled`
An unhandled application error occurred.

Required: `error_code: string`, `surface: string` — use the same enumerated values as `page` in `page.loaded` (e.g. `home`, `board`, `review`), plus component-level values like `capture_modal`, `proposal_card`, `agent_run_detail`. Do not include stack traces or user-generated content.

### `error.api_request_failed`
An API request to the backend failed.

Required: `status_code: number`, `endpoint_pattern: string` — parameterized path pattern only (e.g. `/api/boards/:id`), not the full URL with IDs.

### `error.empty_state_shown`
An empty state was shown where content was expected.

Required: `page: string`, `reason: string` — `no_data`, `load_failed`, `filter_mismatch`

---

## Launch Gate Telemetry Anchors

Each release gate has a set of telemetry signals that constitute evidence of product coherence. These are the minimum signals that should be flowing before promoting to a release gate.

### R1 — Novice-First Beta

Required signal coverage:
- `first_run.wizard_completed` rate — must be measurable
- `page.loaded` for `home`, `today`, `inbox`, `review` — must be non-zero
- `capture.submitted` → `proposal.approved` → `proposal.executed` funnel — must be observable
- `error.empty_state_shown` for core pages — must be low
- `auth_session.started` with `auth_method` breakdown — must work

Exit check: A new user can reach `proposal.executed` within a single `session_id` with no `error.unhandled` on core pages.

### R2 — Agent Foundation Alpha

Required signal coverage:
- All R1 signals
- `agent_run.started`, `agent_run.completed`, `agent_run.failed` — must be emitting
- `agent_run.proposal_linked` — must be emitting for template agent
- `workspace_mode.changed` to `agent` — must be measurable

Exit check: A supervised agent run creates a linked proposal visible in the review queue, traceable through `agent_run.started` → `agent_run.proposal_linked` → `proposal.approved` or `proposal.rejected`.

### R3 — Knowledge/Integrations Alpha

Required signal coverage:
- All R2 signals
- `page.loaded` for `knowledge` — must be non-zero
- `capture.submitted` from inbound/import paths (`source: import`)
- `card.created` with `source: import` — must be measurable

---

## What NOT to Collect (Examples)

The following are concrete examples of what to avoid. These are illustrative, not exhaustive.

| Tempting property | Why not | Safe alternative |
|---|---|---|
| `card_title: "Fix login bug"` | User content / PII risk | Omit entirely |
| `user_email: "user@example.com"` | Direct PII | Omit entirely |
| `username: "john_doe"` | Direct PII | Omit entirely |
| `board_name: "Q2 Sprint"` | User content | Omit entirely |
| `error_message: "Card 'Fix login' not found"` | May contain user content | Use `error_code` only |
| `proposal_diff: "..."` | Confidential content | Omit entirely |
| `search_query: "fix auth bug"` | User content | Omit entirely |
| `input_length: 147` | May allow inference | Use `input_length_bucket` |
| `hostname: "JOHNS-MACBOOK"` | Device PII | Omit entirely |

---

## Implementation Notes

1. **Disabled by default**: The telemetry service should check `settings.telemetry.enabled` (default `false`) before emitting any event. An early return guard at the service level is the safest pattern.

2. **Client-side only initially**: Product telemetry events originate in the frontend. Backend-emitted events (e.g. proposal execution, agent run completion) should be surfaced via the existing metrics/observability stack, not duplicated here until a unified pipeline exists.

3. **Batching**: Events should be batched locally and flushed periodically (e.g. every 30 seconds or on page unload) rather than sent per-event to reduce noise and latency impact.

4. **Reuse existing anchors**: Do not create a separate analytics backend. Reuse the existing `MetricsView`/`metricsStore` pipeline where feasible for board-level signals. Product-level events (navigation, session, capture) require a lightweight event bus.

5. **Event bus pattern**: A simple `telemetry.emit(event, properties)` function with an opt-in guard is sufficient. No heavy SDK required at this stage.

6. **Versioning**: Include `app_version` in the envelope so event schemas can be evolved without historical ambiguity.

7. **Local-first analytics**: All telemetry data, when collected locally, stays local by default. Remote reporting requires explicit configuration.

---

## Related Docs

- `docs/GOLDEN_PRINCIPLES.md` — GP-06 (review-first), GP-08 (product legibility), GP-09 (traceable agent expansion)
- `docs/product/DOGFOODING_GUIDE.md` — manual metrics collection companion
- `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/07_TESTING_METRICS_AND_OPERATIONS.md` — source blueprint for event names and metric definitions
- `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/10_PHASED_ROADMAP_AND_RELEASE_PLAN.md` — R1/R2/R3 release framing
- Issue #77 — broader analytics/dashboard telemetry work
- Issue #328 — first-run smoke and launch-criteria overlap
