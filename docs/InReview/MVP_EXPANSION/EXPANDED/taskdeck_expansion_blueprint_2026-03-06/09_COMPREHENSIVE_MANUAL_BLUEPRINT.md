# Comprehensive Manual Blueprint

Taskdeck is now large enough that it needs a real user manual architecture, not only scattered docs.

## Manual goal

The manual should answer three different kinds of questions:

1. **What is this product for?**
2. **How do I use it for real work?**
3. **How do I use the advanced or future-facing surfaces safely?**

## Suggested manual structure

### Section 1 — Start here

Audience:
- new users

Contents:
- what Taskdeck is in one page
- the 2-minute first value path
- glossary of Projects / Inbox / Review / Today / Agents

### Section 2 — Daily use

Audience:
- novice and regular users

Contents:
- Home
- Today
- Inbox
- Review
- Projects
- daily and weekly routines

### Section 3 — Working inside projects

Audience:
- all users

Contents:
- board/project basics
- cards, labels, comments, due dates, blocked state
- starter packs
- common project templates

### Section 4 — Capture and review

Audience:
- all users

Contents:
- capture sources
- triage
- proposal review
- proposal risk
- provenance and trust model

### Section 5 — Advanced automation and diagnostics

Audience:
- power users

Contents:
- queue
- chat
- activity
- notifications
- ops
- archive
- access

### Section 6 — Agents

Audience:
- advanced users and future customers

Contents:
- what agents are
- what a run is
- policies and review thresholds
- agent templates
- reading run traces

### Section 7 — Integrations and knowledge

Audience:
- advanced users

Contents:
- imports
- webhooks
- knowledge docs
- search
- connector model

### Section 8 — Recipes

Audience:
- everyone

Examples:
- use Taskdeck for engineering sprint planning
- use Taskdeck for content planning
- use Taskdeck for support triage
- use Taskdeck for learning/research
- use Taskdeck with an inbox triage assistant

### Section 9 — Troubleshooting

Audience:
- everyone

Contents:
- why is this page empty?
- why did triage fail?
- why do I need review before apply?
- what does “risk” mean?
- where are advanced pages?
- how do I enable demo/sample workspace?

## Writing rules for the manual

- explain the user goal before the mechanism
- use screenshots and examples heavily
- prefer examples over abstract definitions
- isolate advanced sections clearly
- do not assume familiarity with “proposal-first” language; explain it plainly

## Product docs map recommendation

### Keep in `docs/`

- `USER_MANUAL.md` (or split manual index + chapters)
- `DEMO_PLAYBOOK.md`
- `DOGFOODING_GUIDE.md`
- `TESTING_GUIDE.md`
- `STATUS.md`
- `IMPLEMENTATION_MASTERPLAN.md`

### Split the manual when it gets too large

Recommended chapter files:

- `docs/manual/01_start_here.md`
- `docs/manual/02_home_and_today.md`
- `docs/manual/03_projects_and_cards.md`
- `docs/manual/04_inbox_and_review.md`
- `docs/manual/05_advanced_automation.md`
- `docs/manual/06_agents.md`
- `docs/manual/07_integrations_and_knowledge.md`
- `docs/manual/08_recipes.md`
- `docs/manual/09_troubleshooting.md`

## In-app help mapping

The manual should have short in-app summaries.

Example mapping:

- Home page -> manual chapter 1/2
- Inbox page -> chapter 4
- Review page -> chapter 4
- Queue page -> chapter 5
- Agents page -> chapter 6
- Integrations page -> chapter 7

## Suggested first manual improvements

1. rewrite manual around top-level navigation instead of implementation slices
2. add “When should I use this page?” at top of every section
3. add “Common mistakes” at end of every section
4. add “See also” links between related sections

## Future documentation work

- stakeholder presentation scripts
- recipe packs for vertical use cases
- agent template cookbook
- connector developer guide
- troubleshooting index keyed by error message / state
