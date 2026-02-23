# 06 - Implementation Plan (Waves + Acceptance Criteria)

## Wave 0 (no-code / 1 day): Start using Taskdeck as a CRM today
1) Apply the Outreach Starter Pack manifest (see `OUTREACH_STARTER_PACK_MANIFEST.json`) via API or by adding a small import-manifest UI.
2) Create first 20 contacts as cards.
3) Use due dates for follow-ups.
4) Track outcomes in the Timeline block.

Exit criteria:
- You can run the daily workflow with zero new code.

---

## Wave 1 (1-2 days): Add Import Starter Pack from JSON UI
Goal: apply any manifest locally (not only first-party catalog).

Backend already accepts manifests in `ApplyStarterPackDto.Manifest`.
Frontend: add Import from file/paste JSON to `StarterPackCatalogModal`.

Acceptance criteria:
- Paste manifest JSON -> dry-run -> apply
- Validation errors are readable

---

## Wave 2 (2-4 days): YAML front matter parser + Contact View
Backend:
- add a parser library (or frontend parser) for YAML blocks in card descriptions

Frontend:
- card detail renders structured fields
- editing fields updates YAML block deterministically

Acceptance criteria:
- No corruption of freeform notes
- Round-trip edits are stable
- Unit tests for parser and serializer

---

## Wave 3 (3-6 days): Cadence engine (Done -> next follow-up scheduled)
Approach:
- define cadences as JSON/YAML templates (3-7-21 and similar)
- when a user marks a follow-up card done, Taskdeck offers a proposal to:
  - update contact `last_touch_at`
  - schedule `next_touch_at` (`DueDate`)
  - optionally create a next-step card

Acceptance criteria:
- Deterministic scheduling
- Guardrails: caps and cool-downs
- Integration tests on board operations

---

## Wave 4 (3-7 days): Message drafting as proposals
Use existing LLM provider + chat service patterns:
- provide templates (feedback ask, intro ask, referral ask)
- return 2-3 drafts + follow-up plan suggestion

Acceptance criteria:
- Drafts are generated from user-provided context and contact history
- UI clearly indicates execution mode (manual send path by default)
- Follow-up scheduling is suggested, not forced

---

## Wave 5 (optional): Analytics + scoreboard
- DMs sent this week
- Reply rate
- Calls booked
- Referrals requested
- Beta users acquired

Acceptance criteria:
- Metrics are computed locally from logged interactions (or inferred from card moves)
