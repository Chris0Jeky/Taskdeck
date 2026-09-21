# Board-access mutation ordering

Status: stacked draft PR #3335, 2026-09-21. Parent: PR #3330 at
`c43a5ace7f403f773153cf91a7d62e312aab1b9c`.

## Reproduced defect

`updateAccess` and `revokeAccess` started independently for one access row even
though the API accepts no expected revision and the entity has no configured
concurrency token. Two role changes could therefore commit or settle in an order
that differed from the user's clicks. Update/revoke could also overlap, and a
queued pre-logout intent had no transport-time session check.

A supplemental runner executes the actual production store with only
Pinia/Vue/API/session boundaries stubbed. Against the parent, four ordering and
session schedules fail while the different-access concurrency control passes.
The same five schedules pass after the correction.

## Contract

- One queue exists per `{boardId, accessId}`. Update and revoke for that row run
  in submission order; different rows remain concurrent.
- The first intent starts transport synchronously. A later intent waits for its
  predecessor to finish, regardless of success or failure.
- Each queued operation owns a loading token from submission through settlement,
  so loading does not drop between same-entry operations.
- Immediately before transport, queued work rechecks the initiating session
  epoch. Session replacement clears queue registration and prevents old intent
  from using later credentials.
- A predecessor failure does not cancel the next intent. The next transport
  clears the predecessor's shared error before running.
- Existing successful-mutation read invalidation and stale settlement rules from
  #3330 remain unchanged.

The client queue preserves one client's submission order only. It does not solve
cross-device concurrency; the backend currently exposes no revision precondition.

## Verification and remaining gates

Actual-module red/green: 1/5 schedules passed on the parent, 5/5 after correction.
The preceding twelve read/session ownership schedules also remain 12/12 green.
Changed TypeScript source/tests transpile without diagnostics. Canonical Pinia/
Vitest, lint, project typecheck, build, full hosted CI and independent review are
still required. Because this is stacked work, retarget to current `main` and
requalify after #3330 lands. No merge, release or deployment qualification is
claimed.
