# 03 — UX Flows & Screens (Superuser Experience)

## The “Daily Outreach” dashboard (core screen)
This is your superuser cockpit.

### Data shown
- **Action budget** (e.g., 3 DMs + 5 comments/day)
- **Due today** (cards with DueDate <= today)
- **Overdue**
- **Next 7 days**
- **Campaign focus** (if enabled): referral pipeline / beta users

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
  - “Draft message”
  - “Log interaction”
  - “Schedule next touch”
  - “Move stage” (cold→warm→active→…)

### Nice-to-have
- “Context summary” (LLM can summarize timeline + notes into 3 bullets)

---

## Logging interaction (fast path)
**Goal: 10 seconds.**

Modal fields:
- Channel (default: LinkedIn DM)
- Direction (default: Outbound)
- Summary (required)
- Optional: paste message text
- Outcome (optional)
- Next touch (date picker)

After save:
- append to timeline
- update last_touch_at
- update due date (if next_touch_at set)

---

## Drafting a message (proposal-first)
Flow:
1) pick intent: feedback / intro / referral / beta user / follow-up
2) Taskdeck generates 2–3 drafts + recommended subject line
3) you choose one → copy to clipboard
4) Taskdeck schedules follow-up automatically (e.g., +3 days)

Important: this is **assistive**, not auto-send.

---

## Campaign screen (optional, later)
Campaign = a time-boxed goal:
- “Google referral pipeline (4 weeks)”
- “Taskdeck beta (10 users)”

Shows:
- contacts in campaign
- conversion (DMs → replies → calls → intros → referrals)
- weekly goals

---

## Content pipeline integration
Use Taskdeck’s existing “Content Calendar” blueprint OR a lane inside Outreach board:
- Ideas
- Drafting
- Review
- Scheduled
- Published

When you publish, Taskdeck auto-creates:
- “Reply to comments” task (due: +1 day)
- “Follow-up post” task (due: +7 days)
