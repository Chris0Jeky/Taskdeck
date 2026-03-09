# Manual Structure Guide

Purpose:
- keep the user-manual architecture explicit without turning `docs/` root back into a large mixed bag
- define how `docs/START_HERE.md`, `docs/USER_MANUAL.md`, future manual chapters, and in-app help should relate to one another

Current living entrypoints:
- `docs/START_HERE.md`
  - first-run bridge doc for evaluators and new users
- `docs/USER_MANUAL.md`
  - single-file reference for the shipped `Home` / `Today` / `Inbox` / `Review` / `Boards` shell, including workflows, FAQ, and troubleshooting

This file is a stable structure map, not the source of truth for shipped behavior.
Shipped behavior still belongs in `docs/STATUS.md`, `docs/START_HERE.md`, and `docs/USER_MANUAL.md`.

## Current Help-Center Baseline

The current help-center baseline stays intentionally simple:

- `START_HERE.md`
  - short product framing and the 2-minute first-value path
- `USER_MANUAL.md`
  - the full shipped-shell reference
- in-app help callouts
  - route-level reminders that should map back to the manual structure below

Do not split the manual into many files yet.
The shell is now strong enough to document by navigation, but not broad enough to justify a large chapter tree in active docs.

## Section Shape

The single-file manual should keep this order:

1. Product shape and workspace modes
2. First-run guide
3. Page guide
4. Step-by-step workflows
5. Advanced and operator surfaces
6. FAQ
7. Troubleshooting
8. Demo and seeded workspace

## Future Chapter Shape

If the single-file manual is split later, use this chapter order:

1. `01_start_here.md`
   - what Taskdeck is for
   - the 2-minute first-value path
   - glossary for `Home`, `Today`, `Inbox`, `Review`, `Boards`, and later `Agents`
2. `02_home_and_today.md`
   - `Home`
   - `Today`
   - daily and weekly routines
3. `03_projects_and_cards.md`
   - board or project basics
   - cards, labels, comments, due dates, blocked state
   - starter packs and common templates
4. `04_inbox_and_review.md`
   - capture sources
   - triage
   - proposal review
   - provenance and trust model
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
   - learning or research
   - inbox-triage assistant flows
9. `09_troubleshooting.md`
   - empty pages
   - triage failure
   - review-before-apply reasoning
   - advanced-page discoverability
   - demo or sample-workspace enablement

## In-App Help Mapping

Keep in-app help blocks mapped back to the manual structure:

| Product surface | Current manual target | Future chapter target |
|---|---|---|
| `Home` | product shape, first-run guide, page guide | chapter 1 or 2 |
| `Today` | first-run guide, page guide, workflows | chapter 2 |
| `Inbox` | page guide, capture workflow, FAQ | chapter 4 |
| `Review` | page guide, capture workflow, FAQ | chapter 4 |
| `Boards` | page guide, workflow, troubleshooting | chapter 3 |
| `Notifications` | page guide | chapter 5 |
| `Chat`, `Activity`, `Ops`, `Access`, `Archive` | advanced and operator surfaces | chapter 5 |
| later `Agents` and `Runs` | future-facing only for now | chapter 6 |
| later `Integrations` and `Knowledge` | future-facing only for now | chapter 7 |

## Writing Rules

- explain the user goal before the mechanism
- prefer examples over abstractions
- add `When should I use this page?` near the top of each page-level section
- add `Common mistakes` near the end of each page-level section
- keep advanced and operator sections clearly separated from the normal-user path
- describe planned surfaces as planned; do not blur future work into shipped behavior

## Split Trigger

Keep `docs/USER_MANUAL.md` as one file until at least one of these becomes true:

- the file becomes difficult to scan in one sitting
- the top-level product navigation is stable enough to justify dedicated chapters
- in-app help starts linking to chapter-level manual targets instead of one manual file
