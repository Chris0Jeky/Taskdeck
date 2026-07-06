---
name: taskdeck-demo-regression
description: Validate Taskdeck with the smallest evidence path that proves the change. Use when a task needs seeded demo state, Playwright proof, screenshots, or stakeholder walkthrough evidence.
---

# Taskdeck Demo Regression

Use Taskdeck's demo and regression tooling as evidence, not as a substitute for product truth.

## Read first

Orient via `autodoc/AGENT_INDEX.md` (the seam map) — find your area in its seams table and jump to the entry point. Read only the relevant section of `docs/STATUS.md` (source of truth; ~1.3k lines — never read end-to-end); don't bulk-read `docs/IMPLEMENTATION_MASTERPLAN.md`. Root `CLAUDE.md`/`AGENTS.md` auto-load — don't re-read them.

Read as needed: `docs/product/DEMO_PLAYBOOK.md` and `docs/product/SCENARIOS.md` (this skill's core evidence sources), `docs/TESTING_GUIDE.md`.

## Evidence ladder

Choose the smallest path that proves the change:

1. targeted unit or integration tests
2. targeted Playwright coverage
3. `npm run demo:director:smoke`
4. full seeded or manual demo flow only when stakeholder-proof is actually needed

## Default bias

- prefer deterministic checks first
- prefer the smoke path over a full manual walkthrough
- use screenshots only when they add signal

## Capture for handoff

Record:

- commands run
- pass or fail result
- whether the run used targeted tests, Playwright, smoke path, or full demo flow
- screenshots or artifacts only when they materially help

## Do not use this skill when

- a small code-path change is already fully proven by nearby automated tests
- the task is final doc sync rather than evidence gathering
