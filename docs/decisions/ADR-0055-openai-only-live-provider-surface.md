# ADR-0055: Collapse Supported Live LLM Configuration to OpenAI

- **Status**: Accepted
- **Date**: 2026-08-20
- **Deciders**: Taskdeck maintainer (provider decision recorded in `#1879`)
- **Related**: `#1879`, ADR-0006, ADR-0018, ADR-0032, ADR-0045, ADR-0046

## Context

Taskdeck carried two vendor-specific live adapters, OpenAI and Gemini, even after the product
standardized its supported live path on OpenAI with `gpt-5.6-luna`. Keeping Gemini selectable meant
maintaining a second credential shape, HTTP client, SSRF and egress entry, circuit, demo resolver,
deployment surface, incident path, and privacy disclosure. That extra security and support surface
did not serve the bounded Windows 0.1.x release.

Simply deleting the adapter is unsafe. The existing selection policy sends unknown or invalid
providers to deterministic Mock mode, so a stale Gemini deployment could appear to start normally
while silently no longer using the live provider its operator selected.

Transcript-source triage also makes the privacy boundary important: ordinary capture triage is
local and deterministic, but transcript-source triage may send bounded transcript chunks,
extraction instructions, and pseudonymous attribution metadata to the selected live provider. It
falls back deterministically on failure and still produces proposals that require review, approval,
and explicit execution.

## Decision

1. OpenAI is Taskdeck's supported vendor-hosted live provider. Its default model remains
   `gpt-5.6-luna`, including the reasoning-token headroom already enforced by provider tests.
2. Remove the Gemini adapter and its active settings, dependency-injection, HTTP, circuit-breaker,
   egress, demo, deployment, secret-handling, and documentation surfaces.
3. A case-insensitive Gemini provider selector or a remaining Gemini settings section is a fatal,
   actionable configuration error. It names `OpenAi`, `OpenAiCompatible`, `Ollama`, and `Mock` as
   supported alternatives. It never silently falls back to Mock.
4. Preserve historical free-form provider and model strings in persisted usage/provenance records,
   exports, and compatibility tests. No database migration rewrites old evidence.
5. Preserve generic `x-goog-api-key` redaction and redirect-header stripping. They are general
   defensive controls, not an executable Gemini integration.
6. `OpenAiCompatible` remains available for explicitly configured compatible endpoints, Ollama
   retains its separately documented prototype/local posture, and Mock remains the safe default.
7. Per-user BYOK setup, validation, rotation, and removal UX is a separate post-0.1 decision. This
   removal does not reinterpret MCP `tdsk_` API keys as provider credentials.
8. This decision supersedes only the Gemini-specific live-provider portions of ADR-0006,
   ADR-0018, ADR-0032, and ADR-0046. Their other decisions and historical bodies remain intact.

## Alternatives Considered

**Keep Gemini deprecated but runnable.** Rejected because every runnable provider retains the full
credential, network, test, documentation, and incident-response burden.

**Delete Gemini and let the unknown-provider fallback select Mock.** Rejected because an operator
could believe a live provider was active when Taskdeck had silently changed cost and behavior.

**Rewrite historical provider strings to OpenAI.** Rejected because it would falsify provenance and
usage history and require an unnecessary data migration.

## Consequences

- There is one supported vendor-hosted live setup to document and verify.
- Existing Gemini deployments must change configuration before they can start; the error gives the
  exact supported alternatives.
- Historical records can still say Gemini, and generic Google-key redaction defenses remain.
- Reintroducing Gemini or another vendor-specific adapter requires a new Accepted ADR, a concrete
  product need, provider/privacy terms, credential handling, SSRF and egress enforcement, sanitized
  failure behavior, deterministic fallback tests, and end-to-end operator verification.

## References

- Issue `#1879` — remove the deprecated Gemini provider
- ADR-0006 — mock-default, explicitly gated live providers
- ADR-0045 — transcript-source LLM triage with deterministic fallback and review-first proposals
