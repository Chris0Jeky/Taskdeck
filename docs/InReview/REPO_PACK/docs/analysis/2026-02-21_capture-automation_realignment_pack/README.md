# 2026-02-21 Capture/Automation Realignment Pack (Repo Add)

This directory is a **planning pack** intended to be copied into the Taskdeck repo under:

`docs/analysis/2026-02-21_capture-automation_realignment_pack/`

It is **non-authoritative** by default (per `docs/INDEX.md` governance rules).  
Promote key outcomes into canonical docs when decisions become “real”:

- `docs/STATUS.md` (what is true)
- `docs/IMPLEMENTATION_MASTERPLAN.md` (what is next)
- `docs/MANUAL_TEST_CHECKLIST.md` (how to verify)

## Contents
- `PRODUCT_BRIEF.md` — thesis, scope, success metrics, non-goals
- `AUTONOMY_TRUST_MODEL.md` — autonomy levels, risk tiers, provenance, defaults
- `CAPTURE_PIPELINE_SPEC.md` — backend model + workflow design
- `API_SPEC_CAPTURE.md` — endpoints, request/response, error codes
- `LLM_TRIAGE_CONTRACT.md` — structured-output schema + prompts + validation rules
- `UX_SPEC.md` — UI flows, keyboard interactions, premium UX rules
- `TESTING_AND_VERIFICATION_PLAN.md` — unit/integration/E2E + manual charters
- `SECURITY_PRIVACY.md` — threat model + data handling + provider safety
- `ISSUE_SEEDS.md` — issue list ready to create in GitHub (titles, labels, AC, dependencies)
- `PROMOTION_CHECKLIST.md` — what to update in canonical docs after shipping

## How to use (recommended)
1. Read `PRODUCT_BRIEF.md`.
2. Create an epic issue `CAP-00` (or similar) to track the capture pipeline.
3. Create issues from `ISSUE_SEEDS.md` in dependency order.
4. Implement one vertical slice at a time.
5. After each merged slice, use `PROMOTION_CHECKLIST.md`.

## Notes on governance
Do **not** add new root-level docs for this work unless you intend to keep them active.
Keep detailed specs here until the slice ships, then either:
- promote key content to `STATUS`/`MASTERPLAN`/checklists, or
- archive the pack under `docs/archive/`.
