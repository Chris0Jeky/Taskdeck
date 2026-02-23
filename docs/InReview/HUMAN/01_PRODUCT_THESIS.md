# Product Thesis and Scope

## The real problem (from your original pitch)
You already know Kanban/checklists work psychologically (“gamification” and visible progress reduce stress).
The failure mode is **maintenance overhead**:

- capturing thoughts is effortful and easy to postpone
- when you miss items, you feel behind → guilt → attrition → you stop using the tool
- you want to context switch without fear of forgetting (offload memory reliably)
- initial setup (labels, columns, descriptions) is also “admin work,” so you avoid it

**Taskdeck must eliminate admin work** to prevent this attrition loop.

## Thesis
**Taskdeck is a local-first execution system for developers where capture is near-zero friction and the system maintains the board via reviewable proposals.**

Translation:
- You dump messy inputs (typed notes, pasted transcripts, later voice).
- Taskdeck transforms them into structured candidates.
- Taskdeck generates a **proposal** (diff) of board changes.
- You approve/apply. Nothing silently reorganizes your work (unless you explicitly enable safe autopilot for low-risk actions).

## Category and positioning
- Not “Trello clone.”
- Closer to: **personal workflow OS** (board + structured intake + safe automation).
- For developers: keyboard-first speed, local-first ownership, auditability.

## Ideal early user
Primary:
- solo developer / CS student / indie builder
- wants private/local control
- willing to run a local app (or Docker)
- values speed and low admin overhead

Secondary (future):
- small dev team (requires collaboration/sync, permissions, and stronger ops/compliance)

## Non-goals (for the next 8–12 weeks)
If you try to do these now, you will lose velocity:
- Real-time multi-user collaboration as a flagship feature
- Cross-device sync / CRDT merge
- Plugin marketplace
- “Full autonomy” agentic mode that performs destructive actions without review
- Deep meeting-platform integrations (Zoom/Teams/etc) before the intake pipeline is valuable even with plain text

## What “good” looks like (product requirements)
The product is succeeding when:
- You reliably capture thoughts because it takes <10 seconds.
- You triage without dread (the system proposes structure).
- You trust automation because it is transparent, reviewable, and reversible-ish.
- You keep using it for weeks, not days.

A simple success definition:
- **You use Taskdeck daily for 30 days** without feeling “maintenance tax.”

## Why Taskdeck can win (unique combination)
Many tools can extract tasks from text.
Fewer tools combine:
- local-first ownership and privacy stance
- proposal-first change management
- keyboard-first, premium UX
- deterministic contracts (schemas, tests, audit trails)

Your repo already has the right architecture primitives for this.
