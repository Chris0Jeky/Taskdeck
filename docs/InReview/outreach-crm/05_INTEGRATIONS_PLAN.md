# 05 - Integrations Plan (Practical Sequence)

## 1) LinkedIn imports (recommended baseline)
Use LinkedIn official export features to obtain:
- Connections CSV
- Optional message archive (if included in downloaded archive)

Then import into Taskdeck as contact cards (Option A) or contact entities (Option B).

### Import mapping (connections CSV)
Typical fields:
- First Name / Last Name
- Company
- Position
- Connected On
- Email Address (sometimes blank)

Mapping:
- `display_name = First + Last`
- `company = Company`
- `role = Position`
- `last_touch_at = Connected On` (optional; or store as `connected_at`)
- `email = Email Address` if present

### Deduping
Dedup key order:
- `linkedin_url` (best)
- else: `email`
- else: normalized(`display_name + company`)

## 2) Email + Calendar (optional, later)
Goal: reduce manual logging.

Options:
- ICS export: generate calendar reminders without deep integrations
- Gmail integration: parse labeled emails to auto-create follow-up tasks
- Google Calendar integration: create follow-up events

MVP suggestion: ICS export only (simpler + local-first).

## 3) GitHub signals (optional)
Use GitHub APIs to:
- show stars/releases as signal inputs for posting ideas
- generate release -> post draft workflows

## 4) Connector execution mode (future)
Connector-driven execution should be treated as a separate layer with explicit auth and policy controls.
This keeps planning logic reusable across manual and connected execution modes.
