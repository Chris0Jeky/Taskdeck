# Integration-store read ownership

Status: draft PR #3329, 2026-09-21. Base: `307c3b8b50bec1cb0bfaea3e570a942bcb1d4451`.

## Reproduced defects

The selected connector was guarded only by connector ID. An older A request could
therefore become authoritative again after A → B → A, or after reset followed by
a new request for A. List reads had no identity at all, so reverse settlement or
a post-reset response could replace current state. List and detail reads also
shared one last-settler-wins loading Boolean.

A supplemental runner transpiled and executed the actual store module with only
its framework/API boundaries stubbed. All five original ownership schedules
failed against `main`; the canonical Vitest suite adds the same schedules plus
separate reset success and stale-failure cases.

## Contract

- List and detail are independent read lanes, each with a unique request owner.
- Starting a newer request retires the preceding owner for that lane.
- Reset advances an epoch before clearing visible state.
- Success, failure, toast and final loading settlement require current ownership.
- Loading remains true while either current lane still owns visible work.
- Superseded transport may finish, but cannot mutate store state or messaging.

The integration API, DTOs and mutation behavior are unchanged. This slice does
not claim to invalidate connector mutations that were already sent before reset.

## Verification and remaining gates

The actual-module supplemental suite changed from 0/5 ownership cases passing on
`main` to 5/5 after the correction. TypeScript syntax transpilation passes. The
committed Pinia/Vitest regressions require the repository's pinned Node 24
frontend qualification and exact-head hosted CI. Before review-ready, run lint,
typecheck, build, full coverage tests and independent diff review. No merge,
release or deployment qualification is claimed here.
