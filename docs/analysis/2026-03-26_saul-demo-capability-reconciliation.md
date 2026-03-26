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

The main remaining gap is packaging:
- current hero boards/scenarios are still engineering/support/content facing
- proposal and success copy are improved, but not yet unmistakably Saul-facing
- the repo lacks a committed rehearsal contract for the exact stakeholder demo path

## Capability Map

| Capability | Current State | Where It Exists | Backlog Owner |
| --- | --- | --- | --- |
| `Home -> Inbox/Capture -> Review -> Board` product loop | Shipped | `HomeView.vue`, `InboxView.vue`, `ReviewView.vue`, `TodayView.vue` | Existing productization wave |
| Review-first trust gate | Shipped | `ReviewView.vue`, automation proposal lifecycle, canonical docs | Existing `#326` |
| Deterministic capture triage for checklist input | Shipped | `CaptureTriageService.cs`, capture schema/contracts, capture loop tests | Delivered capture wave |
| Capture/proposal/card provenance | Shipped | `ReviewView.vue`, `InboxView.vue`, `CardModal.vue`, capture/card provenance APIs | Delivered capture wave |
| Proposal readability baseline | Partially shipped | proposal presentation DTO/service + `ReviewView.vue` rendering | Existing `#326` |
| In-app demoability / hero-board attention cues | Partially shipped | `Home`, `Today`, seeded/demo tooling, recent boards/recommended actions | Existing `#330` |
| Deterministic demo reset and orchestration | Shipped | `demo:seed`, `demo:director`, `demo:director:smoke`, JSON scenarios | Delivered demo migration wave |
| Business-facing starter pack / hero blueprint | Missing | current first-party packs are engineering/support/content only | New `#354`; broader pack wave remains `#175` |
| Saul-facing business scenario and exact ACME capture story | Missing | no accounting/client-onboarding scenario is shipped today | New `#354` |
| Explicit rehearsal contract for recording | Missing | no committed pass/fail prep guide for this exact stakeholder path | New `#355` |
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

- Proposal summaries are more readable than raw operations, but still not consistently business-legible on first glance.
- Home/Today already support the core path, but they are not yet curated around a single business demo board.
- Starter-pack/setup UX exists, but the available packs still frame the product around engineering/support/content examples.

### Missing

- one dedicated Saul-facing board blueprint and deterministic scenario
- one exact seeded ACME-style onboarding capture story
- stronger review/success wording that makes the trust model impossible to miss
- one committed rehearsal contract that defines reset command, route order, proof points, and artifacts

## Delivery Strategy

### Phase A: Business Demo Story First

Land the minimum story that changes the whole demo:
- client-onboarding starter pack / blueprint
- deterministic Saul-facing scenario
- exact seeded checklist capture input
- clean board reveal after execution

Primary owner:
- `#354`

### Phase B: Trust And Legibility Hardening

Use existing productization anchors instead of duplicating them:
- proposal summaries, risk/source cues, trust language
- review-first hero path clarity
- in-app demoability and hero-board quality

Primary owners:
- `#326`
- `#330`

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
- `#355` `TST-24`: Saul-facing demo rehearsal contract, acceptance checklist, and artifact guide
- `#356` `DEMO-00`: Saul-facing demo alignment tracker

Reused existing anchors:
- `#175` for broader starter-pack expansion after the pre-demo slice
- `#216` for broader demo script/public framing
- `#326` for proposal readability and trust-cue hardening
- `#330` for in-app demoability and hero-board quality

## Recommended Execution Order

1. `#354`
2. `#326`
3. `#330`
4. `#355`
5. `#216`

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
The remaining work is to make that proof instantly legible to Saul:
- one business story
- one clean trust-first path
- one repeatable rehearsal contract
