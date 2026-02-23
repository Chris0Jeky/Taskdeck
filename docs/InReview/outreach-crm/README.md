# Taskdeck Outreach CRM / Superuser Mode - Design Pack (2026-02-23)

This pack defines a high-throughput outreach operations mode inside Taskdeck.
It is engineering-focused and policy-agnostic: execution behavior is configurable (draft-first/manual by default, connector-driven execution as a future option).

It is designed to slot into Taskdeck's existing primitives:
- Boards / columns / cards / labels / due dates
- Automation proposals + policy engine (review-first)
- Starter Packs (board blueprints)

## What you get
- A concrete product spec (goals, scope boundaries, principles)
- Two implementation options:
  1) Card-first CRM (fastest; minimal DB changes)
  2) Structured CRM module (proper entities; higher effort)
- UX flows + screen list
- Automation controls and throughput guardrails
- Integrations plan (official exports first, connector expansion later)
- Starter Pack manifest JSON for an Outreach CRM board blueprint
- Issue seeds for GitHub

## Where to put these docs
Recommended location: `docs/InReview/outreach-crm/` in your Taskdeck repo.
