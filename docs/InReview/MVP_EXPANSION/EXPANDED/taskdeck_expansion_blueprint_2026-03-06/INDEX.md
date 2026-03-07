# Taskdeck Expansion Blueprint (2026-03-06)

This bundle is a forward-looking product and architecture package for the next major step after the demo-expansion wave.

It is written against the repository snapshot in `Taskdeck-main.zip` and is deliberately shaped around the system that already exists:

- boards / columns / cards / labels
- inbox / capture / triage
- automation proposals
- queue / chat / ops / activity / notifications
- starter packs / imports / webhooks / demo harness / scenario runner

The goal is not to restart Taskdeck from scratch.
The goal is to **turn the existing core into a polished novice-first productivity app and, later, a broad supervised agent workspace**.

## Read order

1. `00_MASTER_BLUEPRINT.md`
2. `01_PRODUCT_STRUCTURE_AND_POSITIONING.md`
3. `02_GOLDEN_PATHS_AND_UX_SHAPE.md`
4. `03_FRONTEND_PRODUCTIZATION_PLAN.md`
5. `04_BACKEND_AND_DOMAIN_EXPANSION_PLAN.md`
6. `05_AGENT_WORKSPACE_ARCHITECTURE.md`
7. `06_INTEGRATIONS_KNOWLEDGE_AND_AUTONOMY.md`
8. `07_TESTING_METRICS_AND_OPERATIONS.md`
9. `08_SEEDED_ISSUES_READY_TO_CREATE.md`
10. `09_COMPREHENSIVE_MANUAL_BLUEPRINT.md`
11. `10_PHASED_ROADMAP_AND_RELEASE_PLAN.md`
12. `11_RISKS_NON_GOALS_AND_DECISION_RULES.md`

## Snippets

Implementation sketches live in `snippets/`.
They are intentionally aligned to the current repo style:

- backend: records/services/controllers in .NET 8 style
- frontend: Vue 3 `script setup`, TypeScript, Pinia-style stores
- scenarios: JSON runner compatible with the current demo/scenario model

These snippets are not drop-in production patches. They are scaffolds meant to reduce design ambiguity.

## Core recommendation in one sentence

Keep one core object model and expose it through **three product modes**:

- **Guided** — novice-first, minimal, self-explaining
- **Workbench** — today’s advanced shell, but better organized
- **Agent** — supervised autonomous workspace built on the same proposal-first substrate
