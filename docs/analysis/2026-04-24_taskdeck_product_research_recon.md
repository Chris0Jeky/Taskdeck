# Taskdeck Product Research Reconnaissance

Date: 2026-04-24
Status: consolidated into canonical research source

The product-research reconnaissance created in commit `3a5a5c86` has been
validated and consolidated.

Use the canonical file:

- `docs/research/PRODUCT_RESEARCH_SOURCE_OF_TRUTH.md`

Use the paste-ready deep-research prompt:

- `docs/research/RESEARCH_BRIEF.md`

Important corrections from the first draft:

- Capture has deterministic fallback extraction, but no structured LLM
  extraction.
- Chat can produce proposal-only writes through structured extraction and
  tool-calling.
- Agent entities, APIs, and UI views exist; the gap is scheduler/templates/user
  value, not total absence.
- Review supports dismissing completed proposals; edit-before-approve remains
  absent.
- Backend chat SSE exists, but the current frontend chat path posts and reloads.
- Telemetry implementation exists, but durable local analytics are not present.
- `AuditRetentionWorker` is claimed in active docs, but source search did not
  find the worker/settings/test source files in this checkout.
