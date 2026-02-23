# Capture Model Decision (CAP-01)

Date: 2026-02-23  
Status: Accepted (implementation baseline for `#200`)

## Decision

Use a **queue-wrapper MVP** for capture persistence:
- store capture items in existing `LlmRequests`
- reserve `RequestType = inbox.capture.v1` for capture artifacts
- store structured capture JSON in `LlmRequest.Payload`

This keeps the external capture contract stable while avoiding early schema churn.

## Why this model now

- Existing infrastructure already provides user ownership, queue lifecycle, worker pickup, and retry behavior.
- It preserves proposal-first trust posture by keeping capture flow inside existing reviewed proposal pipeline.
- It reduces delivery risk for CAP-01/CAP-02 by reusing tested queue/authz code paths.

## Contract introduced in CAP-01

- Canonical capture source enum: `Typed`, `Paste`, `TranscriptPaste`, `Import`, `Voice`, `MeetingIntegration`
- Canonical capture status enum: `New`, `Triaging`, `Triaged`, `ProposalCreated`, `Converted`, `Ignored`, `Failed`
- Status transition policy defined and test-backed (no invalid state skipping)
- Payload contract `CapturePayloadV1` with invariants:
  - schema version fixed to `1`
  - raw text required and max length `20,000`
  - forbidden actor identity fields in payload (`userId`, `ownerUserId`, etc.)
  - optional provenance linkage fields for `capture item -> triage run -> proposal`

## Compatibility behavior

- Existing non-capture queue request types remain unchanged.
- Capture payload parsing is backward compatible with plain text input and normalizes to `CapturePayloadV1`.

## Migration path (when warranted)

If capture volume and query needs outgrow queue-wrapper semantics:
1. Introduce dedicated tables (`CaptureArtifacts`, `CaptureTriageRuns`).
2. Keep `/api/capture/*` contract and status/source enums unchanged.
3. Migrate existing `inbox.capture.*` `LlmRequests` into dedicated entities in batches.
4. Keep `AutomationProposal.SourceReferenceId` linkage stable during and after migration.

This preserves client/API behavior while allowing storage internals to evolve.
