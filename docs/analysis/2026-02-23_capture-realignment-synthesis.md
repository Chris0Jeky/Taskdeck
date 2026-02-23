# 2026-02-23 Capture Realignment Synthesis

Status: Non-authoritative analysis record (promoted outcomes are tracked in active docs and GitHub issues).

## Sources Reviewed

- `docs/InReview/REPO_PACK/docs/analysis/2026-02-21_capture-automation_realignment_pack/`
- `docs/InReview/docs/analysis/2026-02-21_capture-security-performance-addendum/`
- Live GitHub issue state (open + closed) for overlap/deduplication.

## Realignment Decision

Accepted direction:
- Introduce an Inbox-first capture pipeline as a near-term product track.
- Keep proposal-first trust model as non-negotiable (no direct model auto-apply).
- Treat capture security/performance as first-class follow-through, not later cleanup.

Key adaptation:
- In-review packs disagree on persistence shape (queue-wrapper vs dedicated capture entities).
- Decision was not hardcoded in docs; it is tracked explicitly as issue `#200`.

## Overlap and Deduplication Outcome

No prior capture/inbox issue wave existed. New issues were seeded:
- tracker: `#199`
- delivery sequence: `#200` to `#211`
- linked follow-through: `#212`, `#213`

Existing issue reused (not duplicated):
- `#81` SEC-06 rate limiting was updated to include capture endpoints.

Wave index synchronized:
- `#107` updated with `Wave F - Capture Inbox Realignment`.

## Label/Policy Reconciliation

In-review seeds used labels that are not in repository governance (`feature`, `automation`, `worker`, `performance`, `observability`).

Mapped to allowed label set from `docs/GITHUB_PROJECT_AUTOMATION.md`:
- `backend`, `frontend`, `ux`, `testing`, `docs`, `llm`, `security`, `hardening`
- exactly one priority label per issue.

## Scope Boundaries (What Is Not Yet Shipped)

The capture pipeline is backlog-seeded only. It is not part of current shipped runtime behavior.

Most critical open dependencies:
- model/storage decision: `#200`
- API surface: `#201`
- triage worker path: `#204`
- end-to-end proof path: `#210`

## Canonical Promotion Targets

This realignment was promoted in planning form to:
- `docs/STATUS.md`
- `docs/IMPLEMENTATION_MASTERPLAN.md`
- `docs/ISSUE_EXECUTION_GUIDE.md`

Implementation-time promotion remains tracked under:
- `#211` (status/masterplan/testing/manual updates after shipped behavior changes).
