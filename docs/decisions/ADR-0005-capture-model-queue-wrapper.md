# ADR-0005: Capture Model — Queue-Wrapper MVP

- **Status**: Accepted
- **Date**: 2026-02-23
- **Deciders**: Project maintainers

## Context

The capture pipeline needs to persist user input (text, paste, transcript, import, voice) before triage. A dedicated `CaptureArtifacts` table was considered, but the existing `LlmRequest` queue already provides lifecycle management (status transitions, retry, failure tracking) and the triage pipeline already consumes queue items.

## Decision

Store capture items in the existing `LlmRequest` queue with `RequestType = inbox.capture.v1`. The queue provides:

- Status lifecycle: New → Triaging → Triaged/ProposalCreated/Failed
- Retry mechanics for transient failures
- Correlation ID for provenance tracking
- Error message persistence for failure diagnosis

Define a capture contract with:
- **Sources**: Typed, Paste, Transcript, Import, Voice, Meeting
- **Statuses**: New (0), Triaging (1), Triaged (2), ProposalCreated (3), Converted (4), Ignored (5), Failed (6)

Migration path: promote to dedicated `CaptureArtifacts` table when capture-specific queries, retention policies, or volume require it.

## Alternatives Considered

- **Dedicated CaptureArtifacts table**: Cleaner domain model but premature — adds migration, repository, and controller surface for a model that's still evolving.
- **In-memory only**: Loses capture on crash/restart; rejected for local-first persistence guarantee.

## Consequences

- **Positive**: Zero schema migration needed; reuses battle-tested queue lifecycle; capture items naturally flow into the triage worker.
- **Negative**: Capture and LLM queue items share a table, making capture-specific queries less efficient; schema evolution requires careful versioning of `RequestType`.
- **Neutral**: `CaptureStatusPolicy.MapFromQueueStatus()` translates between queue and capture status enums.

## References

- `docs/analysis/2026-02-23_capture-model-decision.md` — full decision document
- `docs/analysis/2026-02-23_capture-realignment-synthesis.md` — capture pipeline design
- CAP MVP loop: `#200` to `#211`
