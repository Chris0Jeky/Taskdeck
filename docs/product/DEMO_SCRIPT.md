# Taskdeck Demo Script (60-120s)

Last Updated: 2026-03-27

## Purpose

A timed narrative storyboard for the core Taskdeck loop. This is the voiceover/presenter script that pairs with the technical rehearsal contract (`SAUL_DEMO_REHEARSAL_CONTRACT.md`).

Target length: 60-120 seconds of screen time.

## Pre-roll

Before recording:

1. Run the canonical bootstrap (see `SAUL_DEMO_REHEARSAL_CONTRACT.md`).
2. Confirm backend + frontend are running.
3. Start at `/workspace/home` in a clean browser window.

## Script

### Scene 1 — Home (0:00-0:15)

**Screen:** `/workspace/home`

**Voiceover:**

> Taskdeck is a local-first execution workspace. Everything runs on your machine — your data never leaves unless you choose to share it.
>
> Home shows you what needs attention right now: items waiting for triage, proposals ready for review, and your active boards.

**Action:** Pause on Home. Point out the Inbox and Review badge counts if visible. Click into the demo board link or navigate to Inbox.

---

### Scene 2 — Capture and Inbox (0:15-0:35)

**Screen:** `/workspace/inbox`

**Voiceover:**

> Capture is designed to be near-zero friction. Paste a messy note, a client email, a checklist — anything. Taskdeck turns it into structured work without changing your board until you say so.
>
> Here we captured an onboarding checklist for a new client. The system triaged it and generated a proposal — a set of board changes you can review before anything happens.

**Action:** Show the ACME capture item. Highlight the proposal handoff link ("Open in Review"). Click it.

---

### Scene 3 — Review (0:35-0:55)

**Screen:** `/workspace/review`

**Voiceover:**

> This is the review gate. Nothing changes on your board until you explicitly approve it. Every proposal shows you exactly what will happen: which cards get created, where they go, and where the idea came from.
>
> This is review-first automation — safe, transparent, and always under your control.

**Action:** Show the proposal detail. Point out the operation headlines, affected entities, and the source/provenance cue. Click "Apply to board."

---

### Scene 4 — Board (0:55-1:10)

**Screen:** `DEMO: Client Onboarding Demo` board

**Voiceover:**

> And here's the result. Clean onboarding tasks on the board, each one traceable back to the original capture. No surprise changes, no phantom cards — just reviewed, approved work.

**Action:** Show the board with the applied tasks. Hover a card to show provenance if visible. Pause for a beat.

---

### Scene 5 — Closing (1:10-1:20)

**Screen:** Stay on board or return to Home.

**Voiceover:**

> That's the core loop: capture, triage, review, apply. Local-first, review-first, keyboard-first. Taskdeck reduces maintenance overhead without taking away control.

**Action:** End on a clean frame (board or Home).

## Timing Budget

| Scene | Duration | Cumulative |
| --- | --- | --- |
| Home | 15s | 0:15 |
| Capture/Inbox | 20s | 0:35 |
| Review | 20s | 0:55 |
| Board | 15s | 1:10 |
| Closing | 10s | 1:20 |

Total: ~80 seconds (within the 60-120s target).

## Key Narratives to Hit

- **Local-first**: data stays on your machine.
- **Review-first**: nothing changes without explicit approval.
- **Capture friction is near-zero**: paste anything, get structured proposals.
- **Provenance**: every card traces back to its source.
- **No unshipped claims**: do not mention features that are not in the current build (e.g., team sync, cloud hosting, mobile).

## What NOT to Say

- Do not claim cloud sync, multi-user real-time collaboration beyond local SignalR, or mobile support.
- Do not promise specific LLM capabilities beyond the mock/deterministic fallback.
- Do not mention pricing, tiers, or availability timelines.
- Do not reference surfaces that are behind feature flags unless you have enabled them for the recording.

## Related

- Technical rehearsal: `SAUL_DEMO_REHEARSAL_CONTRACT.md`
- Demo tooling: `DEMO_PLAYBOOK.md`
- Scenario definitions: `SCENARIOS.md`
