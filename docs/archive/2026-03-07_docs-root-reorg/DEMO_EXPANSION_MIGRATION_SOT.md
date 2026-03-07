# Demo Expansion Migration Source Of Truth (v0-v3)

Last updated: 2026-03-02
Owner: Taskdeck maintainers

## Purpose

This document is the canonical migration reference for porting the external demo expansion code into this repository in controlled batches.

It replaces ad-hoc interpretation of long chat transcripts with:

- versioned source mapping (`v0`, `v1`, `v2`, `v3`)
- file-level change inventory
- batch boundaries and dependency order
- compatibility risks against current `Taskdeck` state

## Canonical Inputs

- Curated summary (current): `docs/archive/2026-03-07_docs-root-reorg/temp_description.txt`
- Curated summary snapshot: `docs/archive/temp_description_curated_2026-03-02.txt`
- Expansion folders:
  - `C:\Users\jekyt\source\TaskdeckDemoExpansion\v0\Taskdeck-main`
  - `C:\Users\jekyt\source\TaskdeckDemoExpansion\v1\taskdeck-advanced`
  - `C:\Users\jekyt\source\TaskdeckDemoExpansion\v2\taskdeck-advanced`
  - `C:\Users\jekyt\source\TaskdeckDemoExpansion\v3\taskdeck-advanced`

## Version Timeline

## v0 (baseline demo kit)

Intent:
- make first-run UX less "empty/scaffolding" and create demo data quickly

Key capabilities:
- `demo:seed` script
- initial demo playbook
- UI declutter defaults:
  - advanced surfaces hidden by default via feature flags
  - Automations nav entry defaults to Proposals
  - Queue composer defaults toward instruction usage

## v1 (advanced kit)

Intent:
- move from one seed script to reusable scenario/autopilot harness

Key capabilities:
- reusable demo library (`demo-lib.mjs`)
- scripted scenarios (`demo-run.mjs` + 3 scenario modules)
- simulated user autopilot (`demo-autopilot.mjs`, heuristic + taskdeck-chat)
- dev-like API walkthrough (`demo/http/taskdeck-demo.http`)
- stakeholder clickthrough spec (`stakeholder-demo.spec.ts`)
- docs for user manual and dogfooding
- Queue board-context UX fix (`boardId` support in composer/types)

## v2 (advanced v2)

Intent:
- make scenarios declarative and autopilot exercise capture/triage pipeline

Key capabilities:
- JSON scenario runner (`scenario-json-runner.mjs`)
- scenario schema (`schema.v1.json`)
- JSON scenarios for engineering/support/content
- autopilot supports capture loop and mixed loop
- scenario docs (`docs/product/SCENARIOS.md`)

## v3 (advanced v3)

Intent:
- one-command, reproducible stakeholder demo with artifacts

Key capabilities:
- demo director orchestrator (`demo-director.mjs`)
- workspace snapshot exporter (`demo-snapshot.mjs`)
- NDJSON tracing across seed/scenario/autopilot flows
- deterministic autopilot option (`--rng-seed`)
- reliability fixes (auth payloads, seed defaults, director-friendly Playwright flow)

## File-Level Delta Inventory

## v0 -> v1 (confirmed diff)

Added:
- `demo/http/taskdeck-demo.http`
- `docs/product/DOGFOODING_GUIDE.md`
- `docs/USER_MANUAL.md`
- `frontend/taskdeck-web/scripts/demo-autopilot.mjs`
- `frontend/taskdeck-web/scripts/demo-lib.mjs`
- `frontend/taskdeck-web/scripts/demo-run.mjs`
- `frontend/taskdeck-web/scripts/scenarios/content-calendar.mjs`
- `frontend/taskdeck-web/scripts/scenarios/engineering-sprint.mjs`
- `frontend/taskdeck-web/scripts/scenarios/support-triage.mjs`
- `frontend/taskdeck-web/tests/e2e/stakeholder-demo.spec.ts`

Modified:
- `docs/product/DEMO_PLAYBOOK.md`
- `docs/INDEX.md`
- `frontend/taskdeck-web/package.json`
- `frontend/taskdeck-web/scripts/demo-seed.mjs`
- `frontend/taskdeck-web/src/types/queue.ts`
- `frontend/taskdeck-web/src/views/AutomationQueueView.vue`

## v1 -> v2 (confirmed diff)

Added:
- `docs/product/SCENARIOS.md`
- `frontend/taskdeck-web/scripts/scenario-json-runner.mjs`
- `frontend/taskdeck-web/scripts/scenarios-json/content-calendar.json`
- `frontend/taskdeck-web/scripts/scenarios-json/engineering-sprint.json`
- `frontend/taskdeck-web/scripts/scenarios-json/schema.v1.json`
- `frontend/taskdeck-web/scripts/scenarios-json/support-triage.json`

Modified:
- `docs/product/DEMO_PLAYBOOK.md`
- `frontend/taskdeck-web/scripts/demo-autopilot.mjs`
- `frontend/taskdeck-web/scripts/demo-lib.mjs`
- `frontend/taskdeck-web/scripts/demo-run.mjs`
- `frontend/taskdeck-web/scripts/scenarios/engineering-sprint.mjs`
- `frontend/taskdeck-web/tests/e2e/stakeholder-demo.spec.ts`

## v2 -> v3 (confirmed diff)

Added:
- `frontend/taskdeck-web/scripts/demo-director.mjs`
- `frontend/taskdeck-web/scripts/demo-snapshot.mjs`

Modified:
- `demo/http/taskdeck-demo.http`
- `docs/product/DEMO_PLAYBOOK.md`
- `frontend/taskdeck-web/package.json`
- `frontend/taskdeck-web/scripts/demo-autopilot.mjs`
- `frontend/taskdeck-web/scripts/demo-lib.mjs`
- `frontend/taskdeck-web/scripts/demo-seed.mjs`
- `frontend/taskdeck-web/scripts/scenario-json-runner.mjs`
- `frontend/taskdeck-web/scripts/scenarios-json/content-calendar.json`
- `frontend/taskdeck-web/scripts/scenarios-json/engineering-sprint.json`
- `frontend/taskdeck-web/scripts/scenarios-json/support-triage.json`
- `frontend/taskdeck-web/tests/e2e/stakeholder-demo.spec.ts`

## v0 Baseline Changes (from transcript + direct checks)

These are foundational changes carried forward into later versions:

- `docs/product/DEMO_PLAYBOOK.md` (introduced in v0)
- `frontend/taskdeck-web/scripts/demo-seed.mjs` (introduced in v0)
- `frontend/taskdeck-web/package.json` (adds `demo:seed`)
- `frontend/taskdeck-web/src/types/feature-flags.ts` (advanced surfaces default to `false`)
- `frontend/taskdeck-web/src/components/shell/AppShell.vue` (Automations nav defaults to `/workspace/automations/proposals`)
- `frontend/taskdeck-web/src/views/AutomationQueueView.vue` (instruction-first composer guidance)

## Current Repository Compatibility Snapshot (2026-03-02)

## Missing in current `Taskdeck`

- all demo scripts (`demo-seed`, `demo-lib`, `demo-run`, `demo-autopilot`, `scenario-json-runner`, `demo-director`, `demo-snapshot`)
- all scenario payloads under `scripts/scenarios-json/`
- legacy JS scenario modules under `scripts/scenarios/`
- `demo/http/taskdeck-demo.http`
- docs: `DEMO_PLAYBOOK.md`, `SCENARIOS.md`, `USER_MANUAL.md`, `DOGFOODING_GUIDE.md`
- `tests/e2e/stakeholder-demo.spec.ts`

## Existing files that differ and need merge strategy

- `frontend/taskdeck-web/src/types/feature-flags.ts`
- `frontend/taskdeck-web/src/components/shell/AppShell.vue`
- `frontend/taskdeck-web/src/types/queue.ts`
- `frontend/taskdeck-web/src/views/AutomationQueueView.vue`
- `frontend/taskdeck-web/package.json`
- `docs/INDEX.md`

## Controlled Batch Plan

Use this order to reduce regression risk and keep each PR reviewable.

1. Batch A (`v0` UX baseline + seed)
2. Batch B (`v1` harness + scenarios + manuals)
3. Batch C (`v2` JSON scenarios + capture-aware autopilot)
4. Batch D (`v3` director + tracing + snapshot + reliability fixes)
5. Batch E (integration hardening: CI wiring, selective enablement, docs/index consolidation)

## Batch Guardrails

- Keep each batch behind explicit scripts/flags where possible.
- Do not auto-enable heavy demo flows in CI by default.
- Preserve current behavior unless the batch explicitly changes UX defaults.
- For overlapping files (`AppShell`, feature flags, queue composer), prefer semantic merge over file replacement.

## Done Definition For The Migration Program

- All batch issues closed in dependency order.
- Demo scripts run successfully on current backend/frontend (`seed`, `run`, `autopilot`, `director`).
- Stakeholder demo artifacts generated from `demo:director`.
- Documentation linked from `docs/INDEX.md`.
- No regressions in existing frontend/backend test suites.
