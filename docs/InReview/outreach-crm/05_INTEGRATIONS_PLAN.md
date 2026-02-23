# 05 — Integrations Plan (Practical + Compliant)

## 1) LinkedIn imports (recommended)
Use LinkedIn’s **official export** features to obtain:
- Connections CSV
- (Optionally) messages archive (if included in your downloaded archive)

Then import into Taskdeck as Contact cards (Option A) or Contact entities (Option B).

### Import mapping (connections CSV)
Typical fields you may receive:
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
Dedup key:
- linkedin_url (best)
- else: email
- else: normalized(display_name + company)

## 2) Email + Calendar (optional, later)
Goal: reduce manual logging.

Options:
- **ICS export**: generate calendar reminders without deep integrations
- **Gmail integration**: parse labeled emails to auto-create “follow-up” tasks
- **Google Calendar integration**: create follow-up events

MVP suggestion: ICS export only (simpler + local-first).

## 3) GitHub signals (optional)
Use GitHub’s APIs to:
- show stars/releases as “signals” for what to post about
- generate a “release → post draft” workflow
Avoid automating fake stars, etc.
