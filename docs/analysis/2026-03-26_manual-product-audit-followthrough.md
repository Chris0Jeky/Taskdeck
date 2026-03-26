# Manual Product Audit Follow-through

Last Updated: 2026-03-26

## Purpose

Turn `docs/analysis/2026-03-26_manual-product-audit.md` into explicit execution ownership without reopening broad roadmap scope.

This document is the reconciliation note for:
- which findings became new GitHub issues
- which findings were routed to existing issues instead of duplicated
- which immediate hardening/documentation changes landed in the same branch

## Wave Mapping

Tracker:
- `#363` `ANL-2026-03-26`: manual product audit follow-through tracker

New issues seeded from the audit:
- `#364` `COL-05`: restore realtime board hub health by fixing credentialed SignalR CORS posture
- `#365` `CAP-23`: keep Inbox triage detail fresh until proposal or another terminal state is reached
- `#366` `UX-20`: align Workbench mode, advanced-nav discoverability, and user-facing docs/copy
- `#367` `UX-21`: decide board-history semantics and align backend queries, UI copy, and manual checklist
- `#368` `AUTO-04`: make chat live-provider status, degraded-mode messaging, and first-turn fidelity explicit
- `#369` `TST-25`: add an opt-in headed manual-audit Playwright pack for operator-visible debugging

Existing issue reused instead of duplicated:
- `#326` `UX-17`: proposal readability and board-centered action flow
  - raw UUIDs / raw target IDs / inline triage-run IDs from the audit were added there as an explicit follow-through note

Not split into a standalone issue because it is corrected directly here:
- stale testing-doc count confidence
  - active docs now carry an explicit audit note instead of pretending the older 2026-03-06 totals are still current

## Immediate Branch-Level Hardening Landed Here

These are not substitutes for the seeded issues above, but they close the most obvious visibility gap immediately:

- backend now exposes `GET /api/llm/chat/health`
- Automation Chat now surfaces provider state explicitly:
  - live provider ready
  - mock provider active
  - degraded/unavailable status
- frontend/API tests now cover the new health contract
- opt-in live-provider Playwright coverage now exists at:
  - `frontend/taskdeck-web/tests/e2e/live-llm.spec.ts`
  - gated by `TASKDECK_RUN_LIVE_LLM_TESTS=1`
- headed local audit commands now exist at:
  - `npm run test:e2e:audit:headed`
  - `npm run test:e2e:live-llm:headed`

## Execution Notes

Priority order inside the wave:
1. `#364` realtime health
2. `#365` Inbox triage freshness
3. `#368` live-provider truthfulness and first-turn fidelity
4. `#366` Workbench/docs truth alignment
5. `#367` board-history semantic alignment
6. `#326` reused review-readability follow-through
7. `#369` headed audit expansion remains intentionally lower priority

Reasoning:
- `#364`, `#365`, and `#368` are the most direct trust/coherence gaps in the live runtime
- `#366` and `#367` are important truthfulness/legibility fixes but are less immediately disruptive than stale triage or ambiguous LLM state
- `#369` is explicitly a debugging/operator aid, not a product blocker

## References

- `docs/analysis/2026-03-26_manual-product-audit.md`
- `docs/STATUS.md`
- `docs/IMPLEMENTATION_MASTERPLAN.md`
- `docs/ISSUE_EXECUTION_GUIDE.md`
