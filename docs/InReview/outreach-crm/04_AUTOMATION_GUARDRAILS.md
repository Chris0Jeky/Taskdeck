# 04 — Automation Guardrails (Stay Safe, Stay Effective)

## Why guardrails matter
The “superuser” goal is throughput, but you must avoid:
- looking like spam
- violating platform rules
- burning your social capital

So you need *hard* rate limits and a human-in-the-loop model.

---

## Guardrails (MVP)
1) **No auto-send**: Taskdeck generates drafts only.
2) **Daily budget**: configurable caps (e.g., 3 DMs/day).
3) **Contact cool-down**: no more than 1 follow-up per contact per N days.
4) **One follow-up rule**: maximum 1 follow-up after initial outreach unless they reply.
5) **Campaign focus**: only surface top N actions/day to avoid scatter.

---

## Guardrails (technical implementation)
### Policy checks (server-side)
- If an automation proposal tries to:
  - create/update > X cards at once → require high-risk confirmation
  - move/delete many entities → reject unless explicitly allowed
- Enforce `MaxOperationCount` style limits for outreach automation too.

### UI checks (client-side)
- “Are you sure?” confirmation when scheduling many follow-ups
- Show “risk label” for action sets (low/medium/high)

---

## Compliance constraints (design implications)
Design for **imports and user-provided inputs**, not scraping or automating platform actions.

- Use **official data exports** (connections CSV, account archive).
- Avoid any component that runs on LinkedIn pages to extract data or automate actions.

---

## Superuser mental model
- Taskdeck is your cockpit.
- LinkedIn/GitHub are execution surfaces.
- Taskdeck proposes; you execute; Taskdeck logs and schedules.
