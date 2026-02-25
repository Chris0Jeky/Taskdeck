# Autonomy and Trust Model
Date: 2026-02-21
Status: Draft (analysis pack; non-authoritative)

## Why this exists
Automation is only valuable if users trust it. The default must be:
- safe
- transparent
- reviewable
- auditable

## Autonomy levels
### Level 0 — Manual
No AI. User creates and edits boards/cards directly.

### Level 1 — Suggest
System suggests:
- task titles/descriptions
- labels
- due dates
- target board/column (optional)

User applies changes manually (no proposal generation required).

### Level 2 — Propose (Default)
System generates a **proposal diff**:
- create/update/move operations
- grouped summary + detailed diff
User must approve/apply.

### Level 3 — Safe Autopilot (Optional; explicit opt-in)
System can auto-apply **low-risk** actions only, with audit trail:
- create Inbox capture artifacts from allowed sources
- apply non-destructive labeling/tagging
- create tasks into Inbox (not into structured boards) without review

Never autopilot (without explicit proposal approval):
- deletes
- archives
- moving across boards
- changing columns/WIP limits
- bulk edits affecting many entities
- import/export operations
- permission/role changes

## Risk tiers for operations
### Tier A — Low risk (candidate for autopilot)
- create Inbox item
- add label to a card (non-destructive)
- update a card description with appended text (non-overwriting) — optional

### Tier B — Medium risk (proposal-required)
- create card in a board column
- create label or column
- move card between columns

### Tier C — High risk (proposal + explicit confirmation)
- archive/unarchive boards or bulk entities
- delete card (hard delete)
- destructive merges or overwrites
- any operation that impacts multiple boards or multiple users

## Provenance model (must-have for trust)
For any card created/edited by automation:
- store reference to source capture artifact (id)
- store a short excerpt used to justify the task
- store triage run id, provider, model, prompt version
- surface provenance in UI (e.g., “Created from Inbox item #123”)

## Audit trail requirements
- every triage run is persisted with status (Succeeded/Failed)
- proposal links back to triage run
- proposal execution results are logged with correlation id

## Privacy posture (local-first)
Defaults:
- mock provider in dev/test
- explicit config gating for live providers
- no automatic exfiltration of capture text to remote providers without opt-in
