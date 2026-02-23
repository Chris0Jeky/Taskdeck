# Execution Roadmap (Step-by-step)

This roadmap is designed to be practical and consistent with Taskdeck’s existing “proposal-first” architecture and clean layering.

## Phase 0 — Finish existing blockers (do not skip)
Before adding new surface area, finish the current “trust breakers” already present in the backlog:
- authentication regressions
- 401/403/404 policy convergence
- centralized error handling consistency
- frontend lint/coverage gates

Reason: capture/automation will amplify inconsistencies. If auth/error semantics feel “off,” users will not trust automation.

## Phase 1 — MVP: Inbox capture (typed) → AI triage → proposal (review-first)
Goal: deliver the core promise without voice/transcription complexity.

Deliverables:
1) Inbox (Capture Artifacts)
- quick capture UI (command palette + hotkey + small modal)
- inbox list view with statuses and search

2) Triage pipeline
- enqueue triage for a capture artifact
- LLM produces structured candidates (strict JSON)
- backend validates schema and transforms into proposal diff

3) Proposal apply
- reuse existing proposal review UI
- apply creates cards/labels/columns as needed (with safe defaults)

Success criteria:
- you can capture messy text and transform it into structured cards in under 60 seconds total
- you are confident nothing is applied without your approval

## Phase 2 — Reduce friction further (still no voice)
Add:
- “Setup assistant” that asks a few questions and generates a starter pack
- “Batch triage” for multiple inbox items
- “Focus mode” for daily execution

## Phase 3 — Optional capture sources (only after Phase 1 retention)
Add:
- paste transcript upload
- voice capture + transcription (local-first options; careful privacy posture)
- integrations (Zoom/Teams/etc) only after you have product-market pull

## Your operating cadence (recommended)
Work in 1–2 week “vertical slices,” each producing:
- user-visible feature
- test coverage additions
- docs updates (STATUS + MASTERPLAN + manual checklist)

Do not start multiple large slices at once.
