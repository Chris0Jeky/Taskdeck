---
name: taskdeck-capture-review-loop
description: Protect the capture -> review -> explicit apply -> board loop - inbox, triage, proposals, approve/reject/execute, provenance, board handoff.
paths: ["backend/**", "frontend/**"]
---

# Taskdeck Capture Review Loop

Protect the central Taskdeck loop:

`capture -> review -> explicit apply -> continue on a board`

## Read first

Orient via `autodoc/AGENT_INDEX.md` (the seam map); root `CLAUDE.md` and region rules auto-load. The capture→review→board seam row in the map names the exact entry points and the preview==Apply (#1235) invariant.

Read as needed:

- feature docs for capture, review, or first-run flows
- the relevant backend or frontend files for the touched slice

## Non-negotiable guardrails

- no silent board mutation from triage or model output
- review remains the trust gate
- provenance stays visible and navigable
- capture stays low-friction
- product language should make the loop easier to understand, not more system-shaped

## Evaluate before changing code

Answer these questions:

- does this reduce or increase capture friction?
- does this keep proposal review explicit?
- does this preserve provenance from capture to proposal to board or card?
- does this make the outcome easier for a user to understand?

## Pairing rule

Use this skill as the semantic guide, then pair it with:

- `taskdeck-backend-slice` when the change lands in API, services, queueing, or execution logic
- `taskdeck-frontend-workspace-slice` when the change lands in UI, routing, or product language

## Verification bias

Prefer a mix of:

- targeted backend or frontend tests for the touched slice
- Playwright coverage when route or interaction behavior changes
- manual sanity check of the golden path when the change is user-facing

## Do not use this skill when

- the work is generic shell or navigation polish with no impact on capture, review, execute, provenance, or board handoff semantics
- the work is only demo harness evidence