# Agent store request and session ownership

Status: draft PR #3338, 2026-09-21. Base: `307c3b8b50bec1cb0bfaea3e570a942bcb1d4451`.

## Reproduced defects

`agentStore` previously let every profile, run-list and run-detail response write its shared surface. Route-clear helpers changed visible values but did not invalidate requests. The store also had no session lifetime, so old-account work could settle after logout/login, including login as the same user ID.

This admitted reverse-settling reads, A-old → B → A-new route reuse, late settlement after route clear, stale error/toast state, false loading clears, and old-session repopulation.

Independent review then exposed two token-refresh boundaries:

1. treating a successful same-user refresh as full identity replacement cleared already loaded agent data;
2. preserving settled data alone still stranded an empty first-load route because the old request was invalidated and the unchanged route did not remount or refetch.

## Contract

Profiles, run lists and run details have independent owner lanes. Each owner carries the current session epoch and a unique request token. A newer request retires only the previous owner in its lane. Route-clear helpers invalidate their own lane before clearing visible state.

User identity, authenticated state and demo-session replacement synchronously advance the epoch, retire all owners, and clear every agent surface, error and loading indicator.

A token-only rotation:

- preserves already loaded profiles, runs and detail;
- retires old-token owners and stale success/failure/toast/loading settlement;
- restarts only active lanes whose visible surface is still empty;
- preserves the exact agent/run parameters captured by the active read;
- never replays a mutation.

Independent lanes remain concurrent. Demo mode retains its no-network behavior. No API, DTO, route, schema, dependency or backend behavior changes.

## Test-first evidence

Initial test-only head `ae52930c6eb5d5b80a2f6a7347242f4ad60a0036` ran the full frontend suite on Ubuntu and Windows. Both platform jobs passed lint, typecheck, production build and PWA validation, then failed in the new real Pinia suite. Ubuntu JUnit recorded **7,159 tests, 8 failures, 0 errors**, all in the intended ownership schedules.

Review-regression head `7b6e137d9625ee6b2ba6ad747a4fdecc5fabe172` added the loaded-data refresh schedule. Ubuntu again passed lint, typecheck, build and PWA validation; all ten ownership cases ran and only the new preservation case failed.

The first corrected head `c48346128982366c0abdf4f1f766246f5cc351dc` then passed Smart CI, Extended and the complete Required CI matrix. Repeat Codex review found the empty-first-load residual described above.

Test-only head `11ab87461ed4773b99af8e35a6625873a52e2425` adds one deferred real-Pinia schedule spanning profiles, runs and detail. A dependency-free runner transpiled and executed the actual production module:

- before the retry correction: each API was called once and all three loading flags became false after rotation;
- after the correction: each API was called twice, old-token settlement was suppressed, and fresh-token results populated all three lanes.

Hosted qualification for the final correction remains authoritative; the supplemental runner does not replace it.

## Remaining gates

Current production correction: `c6a88e78cc9a306992dd95ae28569fb575c4e286` before this documentation commit.

Exact final-head lint, typecheck, production build, complete Vitest on Ubuntu and Windows, Required CI, Extended, Self-Test, and repeat independent review are required. Review should focus on retry capture, route-clear cancellation, no mutation replay, and avoiding refresh loops.

This is client-state integrity, not a claim of server-side authorization bypass or transport cancellation. No merge, release or deployment qualification is claimed.
