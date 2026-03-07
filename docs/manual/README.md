# Manual Structure Guide

Purpose:
- keep the user-manual architecture explicit without turning `docs/` root back into a large mixed bag
- define how `START_HERE.md`, `USER_MANUAL.md`, future manual chapters, and in-app help should relate to each other

Current living entrypoints:
- `docs/START_HERE.md`
  - first-run bridge doc for evaluators and new users
- `docs/USER_MANUAL.md`
  - single-file reference for the current shipped product

This file is a stable structure map, not the source of truth for shipped behavior.
Shipped behavior still belongs in `docs/STATUS.md`, `docs/START_HERE.md`, and `docs/USER_MANUAL.md`.

## Chapter Shape

If the single-file manual is split, use this chapter order:

1. `01_start_here.md`
   - what Taskdeck is for
   - 2-minute first-value path
   - glossary for `Projects`, `Inbox`, `Review`, `Today`, and `Agents`
2. `02_home_and_today.md`
   - `Home`
   - `Today`
   - daily and weekly routines
3. `03_projects_and_cards.md`
   - project/board basics
   - cards, labels, comments, due dates, blocked state
   - starter packs and common project templates
4. `04_inbox_and_review.md`
   - capture sources
   - triage
   - proposal review
   - risk and provenance/trust model
5. `05_advanced_automation.md`
   - queue
   - chat
   - activity
   - notifications
   - ops
   - archive
   - access
6. `06_agents.md`
   - what agents are
   - what a run is
   - policies and review thresholds
   - templates and run traces
7. `07_integrations_and_knowledge.md`
   - imports
   - webhooks
   - knowledge docs
   - search
   - connector model
8. `08_recipes.md`
   - engineering sprint planning
   - content planning
   - support triage
   - learning/research
   - inbox-triage assistant flows
9. `09_troubleshooting.md`
   - empty pages
   - triage failure
   - review-before-apply reasoning
   - advanced-page discoverability
   - demo/sample workspace enablement

## In-App Help Mapping

Keep in-app help blocks mapped back to the manual structure:

| Product surface | Manual target |
|---|---|
| `Home` / `Today` | chapter 1 or 2 |
| `Projects` / board view | chapter 3 |
| `Inbox` / `Review` | chapter 4 |
| `Queue`, `Chat`, `Activity`, `Notifications`, `Ops`, `Archive`, `Access` | chapter 5 |
| `Agents` / `Runs` | chapter 6 |
| `Integrations` / `Knowledge` | chapter 7 |

## Writing Rules

- explain the user goal before the mechanism
- prefer examples over abstractions
- add `When should I use this page?` near the top of each section
- add `Common mistakes` near the end of each section
- keep advanced/operator sections clearly separated from normal-user paths

## Split Trigger

Keep `docs/USER_MANUAL.md` as one file until at least one of these becomes true:

- the file becomes difficult to scan in one sitting
- the top-level product navigation (`Home`, `Today`, `Projects`, `Review`, `Agents`, `Integrations`) is shipped and stable enough to justify dedicated chapters
- in-app help starts linking to chapter-level manual targets instead of a single-file manual
