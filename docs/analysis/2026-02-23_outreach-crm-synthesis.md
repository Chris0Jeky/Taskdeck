# 2026-02-23 Outreach CRM Pack Synthesis

Date: 2026-02-23
Status: Working-note synthesis (non-authoritative planning artifact)
Purpose: Reconcile `docs/InReview/outreach-crm` into canonical docs and dependency-aware backlog items.

Canonical sources of truth remain:
- `docs/STATUS.md`
- `docs/IMPLEMENTATION_MASTERPLAN.md`
- `docs/ISSUE_EXECUTION_GUIDE.md`

## Source materials reviewed

- `docs/InReview/outreach-crm/README.md`
- `docs/InReview/outreach-crm/01_VISION_AND_SCOPE.md`
- `docs/InReview/outreach-crm/02_DATA_MODEL_OPTIONS.md`
- `docs/InReview/outreach-crm/03_UX_FLOWS_AND_SCREENS.md`
- `docs/InReview/outreach-crm/04_AUTOMATION_GUARDRAILS.md`
- `docs/InReview/outreach-crm/05_INTEGRATIONS_PLAN.md`
- `docs/InReview/outreach-crm/06_IMPLEMENTATION_PLAN_WAVES.md`
- `docs/InReview/outreach-crm/07_ISSUE_SEEDS.md`
- `docs/InReview/outreach-crm/08_TEST_PLAN.md`
- `docs/InReview/outreach-crm/09_CONTACT_CARD_EXAMPLES.md`
- `docs/InReview/outreach-crm/OUTREACH_STARTER_PACK_MANIFEST.json`

## Normalization pass applied

The in-review pack was rewritten to use engineering-neutral language:
- removed ethics-framed wording
- removed should/should-not framing tied to automation avoidance
- retained technical constraints as configurable policy and execution-mode decisions

## Key extracted signals

1) Data model path:
- Option A (card-first YAML front matter) is the fastest proof path.
- Option B (structured `Contact`/`Interaction` entities) is the later analytics/productization path.

2) UX shape:
- Daily Outreach dashboard, structured contact detail, and rapid interaction logging are the core loop.

3) Automation shape:
- proposal-first draft generation and cadence scheduling should remain deterministic and test-backed.

4) Integration shape:
- imports and explicit user-provided data paths are baseline; connector execution is a separate later layer.

5) Test shape:
- parser round-trip stability, cadence guardrails, and E2E outreach loop coverage are required for delivery quality.

## Backlog reconciliation decisions

Net-new low-priority wave seeded:
- `#262` OUT-00 wave tracker
- `#263` OUT-01 JSON manifest import path
- `#264` OUT-02 YAML parser/serializer contract
- `#265` OUT-03 structured contact detail + timeline logging
- `#266` OUT-04 cadence scheduling proposal flow
- `#267` OUT-05 daily outreach dashboard
- `#268` OUT-06 outreach draft templates in proposal/chat runtime

Reuse/knowledge-transfer updates (no duplicate issue creation):
- `#75` INT-01 updated with outreach CSV mapping + dedupe-key strategy
- `#77` ANL-01 updated with deferred outreach scoreboard metric hooks
- `#175` PACK-06 updated with outreach starter-pack blueprint alignment note
- `#107` wave index updated with Outreach CRM wave (`#262` to `#268`)

## Priority and sequencing decisions

- Entire outreach wave remains `Priority IV` (deferred maturity tranche).
- Execution order: `#263`/`#264` -> `#265` -> `#266` -> `#267`/`#268`.
- Existing higher-priority tracks (Priority I/II/III) remain ahead by default.

## Canonical-doc promotion map

Promoted to active planning docs:
- deferred Outreach CRM expansion track declaration
- issue IDs and dependency order
- duplicate prevention and reuse links to existing issues

Kept in in-review/analysis scope for now:
- detailed contact-card examples
- full long-form screen specification and optional campaign extensions
