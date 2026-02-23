# Product Brief — “Board that Maintains Itself”
Date: 2026-02-21
Status: Draft (analysis pack; non-authoritative)

## Problem statement
Kanban/task boards reduce stress and improve follow-through, but many users fail to stick with them due to **maintenance overhead**:

- capturing thoughts is effortful → delayed → forgotten
- board hygiene is admin work → avoided
- missed tasks create guilt → attrition loop (“I already know I’ll drop it again”)

## Product goal
Make “keeping track” feel nearly free:
- **Capture** is instant and low-friction.
- **Organization/maintenance** is delegated to the system (AI + rules).
- **Trust** is preserved via proposal-first, review-first changes with provenance.

## Primary user (near-term)
Solo developer/student who:
- wants private/local control
- benefits from keyboard-first workflows
- values low friction and “no cognitive clutter”

## Core promise
**Dump messy inputs → get structured tasks → apply safely.**

## Differentiators
- Local-first storage (user owns data)
- Review-first automation (proposals + diffs)
- Provenance attached to created tasks (“where did this come from?”)
- Keyboard-first UX and fast navigation

## MVP scope (Capture MVP)
In scope:
- typed capture into an Inbox (Capture Artifacts)
- AI triage generates structured candidates
- system generates a proposal diff
- user reviews/applies to board

Out of scope:
- voice recording and transcription (future source)
- meeting-platform integrations
- team collaboration/sync
- autopilot changes without review (except optional safe-label autopilot later)

## Success metrics (for dogfooding)
Activation:
- time-to-first-capture < 10 seconds
- time from capture to applied proposal < 60 seconds

Retention:
- you capture daily for 30 days
- you apply triage proposals multiple times per week

Quality/trust:
- zero “silent changes”
- visible provenance on tasks created via triage
- deterministic failure messages when output is invalid

## Product principles
- Reduce friction before adding features
- Prefer “propose” over “do”
- Make every automation change legible (diff, summary, provenance)
- Design for quick exit (escape-stack contract)
- Minimize guilt loops (Inbox keeps things safe even if you don’t triage immediately)
