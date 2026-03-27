# Saul-Facing Demo Capability Reconciliation

Last Updated: 2026-03-26

## Purpose

Reconcile `docs/WIP/Taskdeck_Demo_Capability_Specification.md` against:
- shipped Taskdeck behavior
- canonical planning docs
- current GitHub backlog ownership

This is a pre-recording planning artifact, not a replacement roadmap.
Its job is to show exactly where the Saul-facing demo stands now, what is already true, what is still missing, and which issues now own the remaining work.

## Executive Summary

Taskdeck is not blocked on core workflow capability.
The repo already ships the hard substrate needed for the stakeholder demo:
- `Home -> Inbox/Capture -> Review -> Board` is already the intended golden path
- capture triage is deterministic enough for checklist-style input
- proposals are review-first and do not auto-apply
- proposal/card provenance is already visible
- deterministic demo seed/director/scenario tooling already exists

Execution status now:
- business-facing hero board/scenario and exact ACME capture story are implemented under `#354`
- demo-critical trust-copy and in-app cue hardening are implemented under demo-critical follow-through in `#326` and `#330`
- a committed rehearsal contract for the exact stakeholder path is now landed as `docs/product/SAUL_DEMO_REHEARSAL_CONTRACT.md` under `#355`
- the remaining pre-recording blocker is broader script framing and narrative stitching in `#216`

## Capability Map

| Capability | Current State | Where It Exists | Backlog Owner |
| --- | --- | --- | --- |
| `Home -> Inbox/Capture -> Review -> Board` product loop | Shipped | `HomeView.vue`, `InboxView.vue`, `ReviewView.vue`, `TodayView.vue` | Existing productization wave |
| Review-first trust gate | Shipped | `ReviewView.vue`, automation proposal lifecycle, canonical docs | Existing `#326` |
| Deterministic capture triage for checklist input | Shipped | `CaptureTriageService.cs`, capture schema/contracts, capture loop tests | Delivered capture wave |
| Capture/proposal/card provenance | Shipped | `ReviewView.vue`, `InboxView.vue`, `CardModal.vue`, capture/card provenance APIs | Delivered capture wave |
| Proposal readability baseline | Demo-critical slice implemented | `AutomationProposalService`, `ReviewView.vue`, `ReviewView` tests | `#326` (demo-critical subset) |
| In-app demoability / hero-board attention cues | Demo-critical slice implemented | `HomeView.vue`, `InboxView.vue`, `BoardView.vue`, view tests | `#330` (demo-critical subset) |
| Deterministic demo reset and orchestration | Shipped | `demo:seed`, `demo:director`, `demo:director:smoke`, JSON scenarios | Delivered demo migration wave |
| Business-facing starter pack / hero blueprint | Implemented | client-onboarding starter pack catalog + setup option + seed/default retargeting | `#354`; broader pack wave remains `#175` |
| Saul-facing business scenario and exact ACME capture story | Implemented | `scripts/scenarios-json/client-onboarding.json`, demo seed/defaults, triage tests | `#354` |
| Explicit rehearsal contract for recording | Implemented | `docs/product/SAUL_DEMO_REHEARSAL_CONTRACT.md` | Implemented via rehearsal contract doc (`#355`) |
| Demo script / narrative framing | Partially planned | broader thesis/demo framing already tracked | Existing `#216` |

## What Is Already Documented

Already represented in canonical docs:
- capture/provenance/review-first substrate in `docs/STATUS.md`
- demo seed/director/scenario tooling in `docs/STATUS.md` and `docs/TESTING_GUIDE.md`
- product-legibility direction in `docs/IMPLEMENTATION_MASTERPLAN.md`
- board-centered trust-first execution order in `docs/ISSUE_EXECUTION_GUIDE.md`

Not previously explicit enough:
- the current gap is business-legible packaging, not missing architecture
- the current hero demo assets are still too developer-facing
- the pre-recording work should be a small focused wave, not a broad roadmap reopening

## Gap Assessment

### Already Present

- Review is already the trust gate.
- Capture triage already supports bullet/checklist input well enough for a controlled demo.
- Provenance and board-aware deep links are already real product behavior.
- Demo reset/orchestration is already strong enough to support a recording workflow.

### Partially Present

- demo-critical slices for `#326` and `#330` are implemented for the Saul-facing wave.
- broader non-demo scope in `#326`/`#330` remains intentionally out of this narrow wave.

### Missing

- one committed rehearsal contract that defines reset command, route order, proof points, and artifacts (`#355`)
- broader reusable demo script/public framing after the product path is stable enough to script truthfully (`#216`)

## Delivery Strategy

### Phase A: Business Demo Story First

Land the minimum story that changes the whole demo:
- client-onboarding starter pack / blueprint
- deterministic Saul-facing scenario
- exact seeded checklist capture input
- clean board reveal after execution

Status:
- implemented under `#354`

### Phase B: Trust And Legibility Hardening

Use existing productization anchors instead of duplicating them:
- proposal summaries, risk/source cues, trust language
- review-first hero path clarity
- in-app demoability and hero-board quality

Status:
- implemented for the demo-critical subset under `#326` and `#330`

### Phase C: Rehearsal Contract

Turn the spec into a repeatable operator workflow:
- exact bootstrap/reset command
- exact route order and visible proof points
- pass/fail checklist before recording
- artifact expectations for rehearsal runs

Primary owners:
- `#355`
- `#216`

## New Issue Wave

Seeded on 2026-03-26:
- `#354` `PACK-08`: Saul-facing client-onboarding starter pack and deterministic demo scenario
- `#355` `TST-24`: Saul-facing demo rehearsal contract, acceptance checklist, and artifact guide (current execution step)
- `#356` `DEMO-00`: Saul-facing demo alignment tracker

Reused existing anchors:
- `#175` for broader starter-pack expansion after the pre-demo slice
- `#216` for broader demo script/public framing
- `#326` for proposal readability and trust-cue hardening (demo-critical subset)
- `#330` for in-app demoability and hero-board quality (demo-critical subset)

## Recommended Execution Order

1. `#354` (implemented)
2. demo-critical subset of `#326` (implemented)
3. demo-critical subset of `#330` (implemented)
4. `#355` (current)
5. `#216` (after `#355`)

Execution rule:
- keep the work pinned to the single stakeholder story
- do not reopen broad architecture or expansion work
- do not let the demo path drift away from `Home -> Inbox/Capture -> Review -> Board`

## Non-Goals For This Slice

Do not spend this wave on:
- broader agent/autonomy features
- ops-console polish
- archive/activity tours
- generic pack expansion beyond what the hero demo needs
- speculative analytics or telemetry breadth beyond what the demo directly needs

## Bottom Line

Taskdeck already proves the thesis technically.
The remaining work is now narrow:
- land the rehearsal contract (`#355`)
- then codify the broader reusable script framing (`#216`)
