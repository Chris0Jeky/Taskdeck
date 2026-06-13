# Taskdeck Legal Documents (Pre-Launch Drafts)

> **Status: DRAFT — NOT LEGALLY BINDING**
> These documents are pre-launch working drafts written by contributors to anchor
> Taskdeck's data-handling, acceptable-use, and sub-processor posture in version
> control. They are **not** a published privacy policy or terms of service, they
> are **not** legally reviewed, and they must not be deployed as the governing
> documents for any hosted Taskdeck instance without review by qualified legal
> counsel. Sections containing placeholder or unverified claims are explicitly
> marked `[LEGAL REVIEW REQUIRED]`.

> **⚠️ NOT IN USE — parked by the 2026-06-13 archive pivot.** This hosted-instance legal package is no longer planned — Taskdeck is personal-use only, never distributed or hosted as a service. Retained only as a template; any self-hosted deployment is the operator’s sole responsibility. See `docs/STATUS.md`.

## Contents

- [`PRIVACY_POLICY.md`](PRIVACY_POLICY.md) — draft privacy policy covering data collected, lawful bases, retention, sub-processors, and user rights.
- [`TERMS_OF_SERVICE.md`](TERMS_OF_SERVICE.md) — draft terms of service covering acceptable use, the beta disclaimer, availability, IP ownership, and termination.
- [`SUB_PROCESSORS.md`](SUB_PROCESSORS.md) — placeholder sub-processor register for the hosted cloud instance.
- [`COOKIE_POLICY.md`](COOKIE_POLICY.md) — short policy describing the storage mechanisms the product currently uses.

## Scope

This directory is intentionally narrow:

- It documents **what the Taskdeck code does today** (e.g., it stores an auth token in
  `localStorage`, it uses SQLite for persistence, LLM providers are off by default)
  so legal review can start from facts rather than invented claims.
- It documents **what an operator of a hosted Taskdeck instance would need to disclose**,
  with placeholders for operator-specific decisions (jurisdiction, hosting region,
  retention periods, DPA counterparties, contact address).
- It does **not** establish a contract, create user-facing rights, or constitute advice.

## How this is meant to be used

### For Taskdeck contributors

- When a change touches data collection, retention, or a new sub-processor, update
  the relevant file here in the same PR. Treat these files like living documentation
  of the platform's privacy surface, not a one-off deliverable.
- Do not add claims you cannot point at in code. If it is aspirational, mark it
  `[LEGAL REVIEW REQUIRED]` or `[NOT YET IMPLEMENTED]`.
- Do not copy text verbatim from other companies' policies — write original drafts
  that describe Taskdeck's actual behavior.

### For an operator launching a hosted Taskdeck instance

1. **Send the documents to qualified legal counsel in your jurisdiction** for review
   and amendment. These drafts are a starting point, not a compliance artifact.
2. Resolve every `[LEGAL REVIEW REQUIRED]` marker: pick a governing jurisdiction,
   set retention periods that match your chosen hosting provider's capabilities,
   identify your Data Protection Officer (or confirm one is not required), and
   name your actual sub-processors.
3. Execute Data Processing Addenda (DPAs) with every sub-processor listed in
   `SUB_PROCESSORS.md` before processing EU/UK personal data through them.
4. If you enable analytics, tracking cookies, or any non-essential tracker, add a
   consent banner and update `COOKIE_POLICY.md` accordingly. The current draft
   reflects the fact that Taskdeck's shipped frontend does not set tracking
   cookies and analytics ship disabled by default.
5. Publish the finalized documents at a stable URL (e.g., `/privacy`, `/terms`)
   before onboarding external users, and keep a changelog so users can see
   material changes.

## Out of scope for this draft

- Publishing the documents on a public domain (requires an operator and a launch milestone).
- Building a cookie-consent banner UI (tracked separately; not required for the
  product's current default surface, which does not use tracking cookies).
- Executing DPAs with sub-processors (requires a named legal entity).
- Choosing a governing jurisdiction, retention schedule, or DPO.
- Drafting a security/incident-response disclosure addendum.

## Cross-references

- `docs/STATUS.md` — source of truth for shipped data-handling features (e.g., GDPR
  data portability / account deletion, delivered in `#83` / `#666`).
- `docs/security/` — security posture, rate limiting, and logging-redaction policies.
- `docs/platform/CONFIGURATION_REFERENCE.md` — which config toggles gate optional
  sub-processors (OpenAI, Gemini, OAuth providers).
- `docs/strategy/03_CLOUD_COLLABORATION_STRATEGY.md` — hosted-cloud evolution plan
  that motivates this draft.

## Status

- **Status:** Pre-launch draft.
- **Last updated:** 2026-04-23.
- **Tracking issue:** `#548` (LEGAL-01).
- **Legal review:** not yet performed.
