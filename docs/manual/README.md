# Manual Structure Guide

Purpose:

- keep the user-manual architecture explicit without turning `docs/` root back into a large mixed bag
- define how `START_HERE.md`, `USER_MANUAL.md`, manual chapters, and in-app help should relate to each other

Current living entrypoints:

- `docs/START_HERE.md`
  - first-run bridge doc for evaluators and new users
- `docs/USER_MANUAL.md`
  - manual index for the shipped shell and chapter map

Manual chapters:

- `docs/manual/01_start_here.md`
- `docs/manual/02_home_and_today.md`
- `docs/manual/03_projects_and_cards.md`
- `docs/manual/04_inbox_and_review.md`
- `docs/manual/05_advanced_automation.md`
- `docs/manual/06_agents.md`
- `docs/manual/07_integrations_and_knowledge.md`
- `docs/manual/08_recipes.md`
- `docs/manual/09_troubleshooting.md`

Shipped behavior still belongs in `docs/STATUS.md`, `docs/START_HERE.md`, `docs/USER_MANUAL.md`, and the relevant chapter files.

## Chapter Shape

1. `01_start_here.md`
   - what Taskdeck is for
   - 2-minute first-value path
   - glossary for `Boards`, `Projects`, `Inbox`, `Review`, `Today`, and `Agents`
2. `02_home_and_today.md`
   - `Home`
   - `Today`
   - daily and weekly routines
3. `03_projects_and_cards.md`
   - board or project basics
   - cards, labels, comments, due dates, blocked state
   - starter packs and the board action rail
4. `04_inbox_and_review.md`
   - capture sources
   - triage
   - proposal review
   - risk and provenance or trust model
5. `05_advanced_automation.md`
   - `Chat`
   - `Queue`
   - `Notifications`
   - `Activity`
   - `Ops`
   - `Archive`
   - `Access`
6. `06_agents.md`
   - future shipped `Agents` and `Runs` surfaces
7. `07_integrations_and_knowledge.md`
   - future shipped imports, connectors, knowledge, and search surfaces
8. `08_recipes.md`
   - concrete step-by-step workflows
9. `09_troubleshooting.md`
   - empty pages
   - triage failure
   - review-before-apply reasoning
   - advanced-page discoverability
   - demo or sample workspace enablement

## In-App Help Mapping

Keep in-app help blocks mapped back to the manual structure:

| Product surface or help topic | Manual target |
|---|---|
| `Home` | `02_home_and_today.md` |
| `Today` | `02_home_and_today.md` |
| `board` | `03_projects_and_cards.md` |
| `Inbox` | `04_inbox_and_review.md` |
| `Review` | `04_inbox_and_review.md` |
| `activity-selectors` | `05_advanced_automation.md` |
| `board-access-selectors` | `05_advanced_automation.md` |
| future `Agents` or `Runs` | `06_agents.md` |
| future `Integrations` or `Knowledge` | `07_integrations_and_knowledge.md` |

## Writing Rules

- explain the user goal before the mechanism
- prefer examples over abstractions
- add `When should I use this page?` near the top of each section
- add `If this page is empty` when the route has a meaningful empty or first-run state
- add `Common mistakes` near the end of each section
- keep advanced or operator sections clearly separated from normal-user paths
- distinguish shipped behavior from future-facing placeholders explicitly

## Ownership And Update Cadence

Update the matching chapter whenever any of these change on a product surface:

- route label or visible navigation placement
- help-callout purpose text
- empty-state guidance or recovery path
- first-run or setup-loop steps
- trust, review, or provenance wording that affects how users are meant to operate the page

When a chapter changes materially, also check:

1. `docs/START_HERE.md`
2. `docs/USER_MANUAL.md`
3. `docs/INDEX.md`

## Split Rule

The manual is now intentionally split because the novice-first shell is shipped and stable enough to justify chapter-level guidance.

Keep `docs/USER_MANUAL.md` as the short manual index.
Keep the detailed guidance in the chapter files under `docs/manual/`.
