# Taskdeck Implementation Masterplan

Last Updated: 2026-02-11  
Planning Horizon: Next 8 to 12 weeks  
Companion Status Doc: `docs/STATUS.md`

## Purpose

This is the active execution guide for sequencing implementation.
Update this file at the end of each meaningful delivery cycle.

## Planning Principles

- `docs/STATUS.md` is authoritative for current reality and test totals.
- Every behavior change ships with tests.
- Keep CI gates aligned with local commands.
- Prioritize one primary Phase 4 track at a time to avoid scope sprawl.
- Build stable interfaces that can later be safely used by an LLM agent.

## Current Cycle Outcome (Completed)

Delivered in this cycle:

1. API integration suite expanded across happy/error paths for boards, columns, cards, and labels.
2. E2E smoke suite expanded from 2 to 5 tests:
   - WIP rejection
   - card move flow
   - board settings lifecycle
3. CLI expanded:
   - `boards update`
   - `columns create`
   - `cards list`
   - improved usage/error handling and exit code semantics
4. CI gate split and hardening completed:
   - backend unit
   - API integration
   - frontend unit
   - E2E smoke
   - Ubuntu/Windows matrix for non-E2E gates

## Roadmap by Horizon

### Horizon A (Week 1 to 2): Stabilize and Close Test Coverage Gaps

- Add remaining API integration negative paths (cross-resource not-found, validation edge cases, conflict variants).
- Expand E2E from smoke to reliability checks for drag/drop ordering and modal validation states.
- Add dedicated tests for CLI command parsing and behavior contracts.
- Monitor first CI matrix runs and close platform-specific drift quickly.

Exit Criteria:
- CI matrix consistently green.
- API integration + E2E suites cover key regressions and primary failures.
- CLI changes are test-backed.

### Horizon B (Week 3 to 6): Complete CLI Primary Track

- Expand CLI command surface toward full board operations:
  - boards: archive/unarchive ergonomics, optional delete path handling
  - columns: update/delete/reorder
  - cards: update/delete/search refinements
  - labels: list/create/update/delete
- Add structured output mode (`--json`) to support machine automation.
- Improve CLI UX:
  - consistent formatting
  - explicit error categorization
  - deterministic exit behavior

Exit Criteria:
- CLI can perform end-to-end daily task operations.
- CLI supports both human-readable and automation-friendly output.

### Horizon C (Week 7 to 12): Agent-Compatible Automation Foundation

Target end state:
- Local LLM agent can act on the board through tool calls driven by text and voice transcript inputs.

Required foundation work:
1. Action Proposal Layer
   - agent proposes operations, does not auto-apply by default.
   - operations represented as explicit, typed mutation intents.
2. Review and Approval UX
   - pending action queue in UI.
   - user can accept, edit, or reject proposed mutations.
3. Diff Visibility
   - show before/after snapshots for board, column, and card mutations.
   - expose concise audit entries for what changed and why.
4. Security and Fallback
   - policy gates for destructive actions.
   - scoped permissions for tool calls.
   - rollback or compensating action path for failed/undesired mutations.
5. Interface Contract
   - stable API and CLI command contracts suitable for automated callers.
   - idempotency and conflict handling defined for repeated agent actions.

Exit Criteria:
- Agent action flow is safe-by-default and reviewable.
- Mutation diffs and audit trail exist for all agent-originated changes.
- Core operations are consumable through automation-compatible interfaces.

## Active Backlog (Prioritized)

1. P0: Add API integration tests for remaining edge/error mappings.
2. P0: Add 3 to 5 additional E2E tests for highest-risk board interactions.
3. P0: Add CLI automated tests and start `--json` output mode.
4. P1: Finish core CLI command parity for columns/cards/labels.
5. P1: Define action proposal schema for future LLM tool-calling integration.
6. P1: Draft approval/diff UX spec for agent-proposed changes.
7. P2: Start time-tracking design spike after CLI milestone lock.

## Next Best Steps (Updated)

1. Add API integration tests for:
   - `PATCH` not-found paths across cards/columns/labels where still missing
   - validation edge cases (empty names, invalid positions, invalid colors)
   - conflict mappings tied to constrained delete/move operations
2. Add E2E tests for:
   - intra-column reorder behavior
   - board filter combinations + persistence expectations
   - keyboard-only card open/create flow regression checks
3. Add CLI contract tests and implement `--json` for:
   - `boards list`
   - `cards list`
4. Create an initial agent action proposal spec:
   - operation envelope
   - dry-run preview response
   - approval token flow

## Weekly Cadence

- Start of week:
  - reconcile `docs/STATUS.md`
  - select top 3 backlog items
- During week:
  - ship vertical slices with tests
  - avoid creating extra top-level planning documents
- End of week:
  - update this file with completed items and reprioritized next steps

## Risk Register

- Risk: CI matrix instability or flaky browser tests
  - Mitigation: deterministic test setup, pinned versions, and quick flake triage
- Risk: CLI scope broadens before reliability hardening
  - Mitigation: test-first additions and `--json` contract discipline
- Risk: Unsafe autonomous agent operations
  - Mitigation: proposal-first flow, explicit approvals, diff visibility, and rollback path
- Risk: Documentation drift
  - Mitigation: keep only `STATUS.md` + this file authoritative
