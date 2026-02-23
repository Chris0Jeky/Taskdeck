# Taskdeck — Testing + Guardrails + Harness Engineering Improvements (2026-02-23)

This pack is meant to be copied into your repo (recommended location: `docs/analysis/`).

It contains:
- A concrete review of your current test suite + CI guardrails and what’s already strong.
- A prioritized improvement plan (quick wins → medium → long-term).
- A backlog of test scenarios (unit/integration/E2E) with explicit acceptance criteria.
- A “Harness Engineering” alignment + gaps doc (based on OpenAI’s Feb 2026 write-up).
- Issue seeds you can paste into GitHub as you convert the plan into execution.

## Suggested integration into your repo

1) Copy the folder contents into:
   - `docs/analysis/2026-02-23-testing-harness/`

2) Add a link from `docs/INDEX.md` under an “Analysis” section.

3) Create the Wave 1 issues from:
   - `ISSUE_SEEDS_TESTING_AND_HARNESS_WAVE_1.md`

## How to use this pack

- Read `01_TESTING_REVIEW_AND_PRIORITIES.md` first.
- Then pick a wave from `02_TEST_SCENARIOS_BACKLOG.md` (start with *Wave 1*).
- Use `03_TESTING_ARCHITECTURE_VNEXT.md` as the “how we write tests here” standard.
- Use `04_GUARDRAILS_AND_GATES_PLAN.md` for CI/quality gates decisions.
- Use `05_OPENAI_HARNESS_ENGINEERING_ALIGNMENT.md` if you want to keep tightening your agent-first posture.
