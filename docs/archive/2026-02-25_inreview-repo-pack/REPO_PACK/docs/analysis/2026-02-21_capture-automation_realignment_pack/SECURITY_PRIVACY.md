# Security and Privacy Notes — Capture + AI
Date: 2026-02-21
Status: Draft (analysis pack; non-authoritative)

## Why this matters
Capture artifacts may contain:
- credentials
- private personal data
- meeting transcripts
- client/work information

A local-first tool must treat this content as sensitive by default.

## Threat model (practical)
Risks to address early:
- raw capture text logged or leaked through errors
- capture text sent to remote LLM providers without explicit consent
- local LLM servers accidentally exposed to LAN/WAN
- XSS/CSRF leading to token/session compromise (existing token-storage hardening work)

## Data handling rules (must-haves)
- Never log `RawText` in normal logs.
- Avoid including `RawText` in error messages.
- List endpoints return excerpts, not full text.
- Store provider prompts/outputs carefully; avoid storing full prompt if it contains raw text unless needed for debugging (config-gated).

## Provider posture
Default:
- `Mock` provider in dev/test.
- Live providers require explicit config gates.

Remote provider (OpenAI):
- Must be explicit opt-in by user.
- UI should disclose when text will be sent externally.

Local provider (example: Ollama):
- docs must warn: bind to localhost only
- detect suspicious configs (e.g., base URL not localhost) and warn in UI
- rate limit and timeout provider calls to avoid blocking the worker

## Token storage
This work intersects with `SEC-12 session-token storage hardening plan` (issue #156).
Do not expand capture features while leaving token storage in a known-risk posture if you plan to deploy publicly.

## Auditability
- Every triage run should record provider + model + promptVersion.
- Every proposal created from triage should carry references (artifactId, triageRunId).

## Data lifecycle controls (future)
- “Purge raw text after conversion” option
- export/import includes capture artifacts (or explicit toggle)
- per-artifact “redact text” action
