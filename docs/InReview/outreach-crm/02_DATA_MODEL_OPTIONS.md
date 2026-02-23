# 02 — Data Model Options (Card-first vs Structured)

Taskdeck already has:
- `Card` with `Title`, `Description`, `DueDate`
- labels, columns, boards
- automation proposals (operations that create/update/move cards)

This enables a **Card-first CRM** with almost no persistence changes.

---

## Option A (recommended first): Card-first CRM (“Contacts as Cards”)

### How it works
- Each Contact is a **Card** on a dedicated “Outreach CRM” board.
- The Card `Description` contains **YAML front matter** with structured contact fields.
- The card `DueDate` is the `next_touch_at` (single source of truth for follow-ups).
- Interactions are appended to the card description in a structured “Timeline” section (or stored as checklist items if you prefer short format).

### Contact card schema (YAML front matter)

```yaml
---
type: contact
display_name: "Jane Doe"
relationship_tier: "A"   # A|B|C
company: "Google"
role: "SRE"
location_tz: "Europe/London"
handles:
  linkedin_url: "https://www.linkedin.com/in/jane-doe/"
  github: "janedoe"
  email: "jane@example.com"
tags:
  - google
  - platform
  - referral-target
source: "GE colleague"
status: "warm"           # cold|warm|active|referral|interviewing|closed
cadence_id: "warm-3-7-21"
last_touch_at: "2026-02-20"
next_touch_at: "2026-02-27"
notes_private: "Met at X; cares about reliability; likes concise messages."
---
```

### Timeline block format (append-only)
Below the YAML block, maintain:

```md
## Timeline
- 2026-02-20 (LI DM, outbound): Asked for 10 min feedback on Taskdeck demo. Outcome: replied, scheduled call.
- 2026-02-22 (Call): Discussed team. Next: ask for intro to hiring manager after applying.
```

### Pros
- Very fast to implement
- Uses existing Taskdeck entities and due-date behaviors
- Works with existing automation proposal operations

### Cons
- Querying/analytics is harder (needs parsing)
- Timeline is text-based until structured entities exist

### Migration path
You can later add `Contact` / `Interaction` tables and migrate by parsing YAML blocks.

---

## Option B: Structured CRM module (proper entities)

Add domain entities:

### `Contact`
- `Id` (Guid)
- `OwnerUserId` (Guid)
- `DisplayName` (string, 200)
- `Company` (string, 200?)
- `Role` (string, 200?)
- `RelationshipTier` (enum A/B/C)
- `Status` (enum)
- `Timezone` (string)
- `LinkedInUrl` (string, 500?)
- `GithubUsername` (string, 100?)
- `Email` (string, 254?)
- `LastTouchAt` (DateTimeOffset?)
- `NextTouchAt` (DateTimeOffset?)
- `CadenceId` (string?)
- `Tags` (separate table or JSON)
- `NotesPrivate` (string, encrypted-at-rest optional)

### `Interaction`
- `Id`
- `ContactId`
- `OccurredAt`
- `Channel` (enum: LinkedIn, Email, Call, InPerson, GitHub, Other)
- `Direction` (Inbound/Outbound)
- `Summary` (string, 500)
- `Body` (string, optional)
- `Outcome` (enum: NoReply, Replied, ScheduledCall, Referred, Other)
- `NextActionAt` (DateTimeOffset?, optional)

### `Cadence` + `CadenceStep`
- `Cadence`: `Id`, `Name`, `Description`
- `Step`: `CadenceId`, `Sequence`, `OffsetDays`, `TemplateId`, `StopIfReplied`

### Pros
- Real analytics, filtering, dashboards
- Cleaner model for future integrations (email/calendar)

### Cons
- More code + migrations + UI
- Higher testing burden

---

## Recommendation
Start with **Option A** for 2–4 weeks to prove the loop.
Then adopt Option B if:
- you want analytics and segmentation
- you want real campaign tracking
- you want to ship this as a product feature
