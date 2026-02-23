# 07 - Issue Seeds (GitHub-ready)

## OUTREACH-001 - Outreach CRM starter-pack blueprint
Type: feature / docs
Goal: Provide a board blueprint for outreach workflow.
Acceptance criteria:
- Manifest validates against schema v1.0
- Creates columns + labels + templates + seed cards
- Included in docs and usable via API apply

---

## OUTREACH-002 - Import starter-pack manifest from JSON in UI
Type: feature (frontend)
Files: `frontend/taskdeck-web/src/components/board/StarterPackCatalogModal.vue`
Acceptance criteria:
- Paste JSON modal
- Dry-run supported
- Clear error messages for invalid manifest
- Tests: component + API mocking

---

## OUTREACH-003 - YAML front matter parser/serializer for contact cards
Type: feature (shared utility)
Acceptance criteria:
- Parse YAML front matter + preserve rest of description
- Serialize with stable formatting
- Unit tests: round-trip, edge cases, invalid YAML fallback

---

## OUTREACH-004 - Contact view: render + edit structured fields
Type: feature (frontend)
Acceptance criteria:
- Contact fields editable via UI
- Updates YAML front matter only
- DueDate syncs to `next_touch_at`

---

## OUTREACH-005 - Cadence engine (follow-up scheduling)
Type: feature (backend + frontend)
Acceptance criteria:
- Define cadence templates (3-7-21 and similar)
- Mark outreach done triggers proposal: set next-touch due date
- Guardrails: per-day budget + contact cool-down
- Tests: unit + integration

---

## OUTREACH-006 - Draft message proposals (LLM)
Type: feature (backend)
Acceptance criteria:
- Generates multiple drafts for selected intent
- Supports configurable execution mode (manual path default)
- Stores drafts as chat messages or proposal output
- Audit logs record generation

---

## OUTREACH-007 - Outreach dashboard (Today view)
Type: feature (frontend)
Acceptance criteria:
- Lists due outreach tasks
- Shows daily budget + remaining quota
- Keyboard-first actions
