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
- Keep scaffolding tracks as side work unless explicitly promoted to the primary track.

## Current Cycle Outcome (Completed)

Delivered in this cycle:

1. API integration suite expanded across happy/error paths for boards, columns, cards, and labels.
2. Nested-route board ownership checks added for cards, columns, and labels.
3. E2E smoke suite expanded from 5 to 8 tests:
   - column reorder
   - keyboard-only open/close flow
   - filter persistence in-session
4. Escape-key closure behavior fixed across major modals and inline add-card forms.
5. CLI expanded and hardened:
   - `boards update`
   - `columns create`
   - `cards list`
   - `--json` output mode (camelCase)
6. CLI contract tests added (`backend/tests/Taskdeck.Cli.Tests`).
7. CI gate hardening completed:
   - backend unit (including CLI contract tests)
   - API integration
   - frontend unit
   - E2E smoke
   - Ubuntu/Windows matrix for non-E2E gates
8. Parallel scaffolding package delivered for future multi-user/export-import/history/queue tracks:
   - domain entities, repository contracts, service interfaces
   - infrastructure repositories and migration
   - foundational domain tests

Note:
- Item 8 is intentional side-track prep and does not replace the primary roadmap sequence below.

## Roadmap by Horizon

### Horizon A (Week 1 to 2): CI and Reliability Consolidation

- Validate first real GitHub matrix runs and patch drift quickly.
- Add residual API negative-path cases not yet covered.
- Extend E2E beyond smoke to multi-step long-session regressions.
- Expand CLI JSON contract to more commands and error payloads.

Exit Criteria:
- CI matrix consistently green.
- API integration + E2E suites cover key regressions and primary failures.
- CLI automation contracts are test-backed.

### Horizon B (Week 3 to 6): Complete CLI Primary Track

- Expand CLI command surface toward full board operations:
  - boards: archive/unarchive ergonomics, optional delete path handling
  - columns: update/delete/reorder
  - cards: update/delete/search refinements
  - labels: list/create/update/delete
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

## Parallel Track (Sidecar): Scaffolding Activation Plan

This track remains sidecar until primary Horizon A/B stability goals are met.

Prepared foundations:
- Multi-user roles and board-level access model
- Export/import interfaces for board/database portability
- LLM queue entities/contracts for offline submission and later processing
- Audit/history foundations

Activation gates:
1. Horizon A exit criteria are met.
2. CLI reliability and JSON contract work is stable.
3. Explicit roadmap re-prioritization is approved.

## Active Backlog (Prioritized)

1. P0: Monitor first GitHub CI matrix runs and patch platform drift.
2. P0: Extend CLI JSON to include structured error payloads.
3. P0: Add remaining low-frequency API edge/error mappings.
4. P1: Finish core CLI command parity for columns/cards/labels.
5. P1: Define action proposal schema for future LLM tool-calling integration.
6. P1: Draft approval/diff UX spec for agent-proposed changes.
7. P2: Prepare implementation sequence for scaffolding services (auth, permissions, export/import, queue) without promoting them to primary track yet.
8. P2: Start time-tracking design spike after CLI milestone lock.

## Next Best Steps (Updated)

1. Verify and monitor first GitHub matrix runs in repository Actions UI.
2. Implement structured JSON error output for CLI (`code`, `message`, `usageHint`).
3. Add long-session E2E regression slice (multi-edit, reorder, filter, refresh persistence).
4. Create initial agent action proposal spec:
   - operation envelope
   - dry-run preview response
   - approval token flow
5. Document scaffolding activation checklist so future work can start quickly when promoted.

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
  - Mitigation: deterministic test setup, pinned versions, and fast flake triage
- Risk: CLI scope broadens before reliability hardening
  - Mitigation: test-first additions and strict JSON contract discipline
- Risk: Unsafe autonomous agent operations
  - Mitigation: proposal-first flow, explicit approvals, diff visibility, rollback path
- Risk: Side-track scaffolding displaces primary delivery goals
  - Mitigation: activation gates and explicit re-prioritization before promotion
- Risk: Documentation drift
  - Mitigation: keep only `STATUS.md` + this file authoritative
