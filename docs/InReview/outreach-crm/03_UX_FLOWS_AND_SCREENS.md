# 03 - UX Flows and Screens (Superuser Experience)

## The Daily Outreach dashboard (core screen)
This is the superuser cockpit.

### Data shown
- Action budget (for example: 3 DMs + 5 comments/day)
- Due today (cards with DueDate <= today)
- Overdue
- Next 7 days
- Campaign focus (optional): referral pipeline / beta users

### Actions
- `J`/`K` to move through items
- `Enter` to open contact card
- `G` to generate message draft (proposal)
- `L` to log interaction (quick modal)
- `S` to schedule follow-up (sets DueDate)
- `D` to mark step done and auto-create next step (cadence)

---

## Contact detail view
### Must-have
- Structured fields parsed from YAML front matter
- Last touch / next touch (and ability to update)
- Timeline list (append-only log)
- Buttons:
  - Draft message
  - Log interaction
  - Schedule next touch
  - Move stage (cold -> warm -> active -> ...)

### Nice-to-have
- Context summary (LLM summarizes timeline + notes into 3 bullets)

---

## Logging interaction (fast path)
Goal: 10 seconds.

Modal fields:
- Channel (default: LinkedIn DM)
- Direction (default: Outbound)
- Summary (required)
- Optional: paste message text
- Outcome (optional)
- Next touch (date picker)

After save:
- append to timeline
- update `last_touch_at`
- update due date (if `next_touch_at` set)

---

## Drafting a message (proposal-first)
Flow:
1) pick intent: feedback / intro / referral / beta user / follow-up
2) Taskdeck generates 2-3 drafts + recommended subject line
3) choose one -> copy to clipboard
4) Taskdeck can suggest the next follow-up window (for example +3 days)

Execution mode note:
- MVP default mode is draft generation + manual send path.
- Connector-driven execution can be added later behind explicit feature flags/policy.

---

## Campaign screen (optional, later)
Campaign = a time-boxed goal:
- Google referral pipeline (4 weeks)
- Taskdeck beta (10 users)

Shows:
- contacts in campaign
- conversion (DMs -> replies -> calls -> intros -> referrals)
- weekly goals

---

## Content pipeline integration
Use Taskdeck's existing Content Calendar blueprint or a lane inside Outreach board:
- Ideas
- Drafting
- Review
- Scheduled
- Published

When you publish, Taskdeck creates:
- Reply to comments task (due: +1 day)
- Follow-up post task (due: +7 days)
