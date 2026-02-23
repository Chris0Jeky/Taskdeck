# Promotion Checklist (After Shipping Any Slice)
Date: 2026-02-21

Use this after each meaningful merge that changes user-visible behavior.

## Required updates
- `docs/STATUS.md`
  - add or adjust “Current Implementation Snapshot”
  - update “Known Gaps and Risks” if relevant
  - update verified test totals if you ran them

- `docs/IMPLEMENTATION_MASTERPLAN.md`
  - add new issues to sequencing
  - record completed items in “Current Cycle Outcome”

- `docs/MANUAL_TEST_CHECKLIST.md`
  - add manual checks for new flows
  - update expectations if behavior changed

- `docs/TESTING_GUIDE.md`
  - add new test commands if needed
  - update coverage mapping if lanes change

## Recommended updates
- `README.md` (only if onboarding changes)
- `docs/INDEX.md` (only if you added new root-level docs; avoid by default)
- archive this planning pack when it becomes stale: `docs/archive/<date>_capture-pack/`

## Verification evidence (required in PR)
- Commands run and results:
  - backend tests
  - frontend unit + build
  - E2E smoke (if touched)
- Screenshots/clips for UX changes
- Note any known limitations or follow-ups
