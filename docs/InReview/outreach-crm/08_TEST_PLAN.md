# 08 — Test Plan (so this doesn’t rot)

## Unit tests (backend)
- Cadence schedule function:
  - given `last_touch_at` and `cadence_id`, compute `next_touch_at`
  - deterministic, time-zone safe
- Guardrails:
  - daily budget enforcement
  - contact cool-down enforcement
- Starter Pack manifest validator already exists; add tests for Outreach manifest if added to first-party catalog.

## Unit tests (frontend)
- YAML parser/serializer:
  - round-trip stable formatting
  - invalid YAML fallback to plain text

## Integration tests (API)
- Apply starter pack to board (dry-run + apply)
- Create a contact card
- Update due date and verify persisted
- Append timeline entry and verify persisted

## E2E tests (Playwright)
- Apply Outreach blueprint
- Create a contact card
- Set due date
- Verify “Due today” shows on dashboard
- Generate message draft (mock LLM)
- Log interaction and schedule next follow-up

## Non-functional tests
- Performance: parsing YAML on large cards
- Security: ensure contact notes are not exposed in export unless user chooses
