# Board-access read and session ownership

Status: draft PR #3330, 2026-09-21. Base: `307c3b8b50bec1cb0bfaea3e570a942bcb1d4451`.

## Reproduced defects

A board-access read could settle after a confirmed grant, update or revoke and
replace that newer client state. Same-board reads were last-response-wins, one
shared loading Boolean could clear while other boards still loaded, and pending
reads or mutations retained permission to publish after session replacement.

A supplemental runner transpiled and executed the actual store module with only
Pinia/Vue/API/session boundaries stubbed. Seven original schedules failed on
`main`: revoke, update, reverse reads, independent loading, same-user session
replacement, old-session mutation and stale failure. The committed Vitest suite
also covers grant settlement.

## Contract

- One read owner exists per board; unrelated boards remain concurrent.
- A successful mutation advances that board's generation and retires older reads.
- Session identity/auth/demo transitions synchronously advance an epoch, clear
  cached access, retire operations and reset loading/error.
- Success, failure, toast and cache writes require the initiating session epoch.
- Loading is derived from current operation tokens, not whichever call settles.
- A stale call still resolves or rejects to its caller; it loses only permission
  to alter the replacement session's UI state.

Server authorization remains authoritative. This corrects truthful client cache
behavior and does not claim a server-side authorization bypass. Same-board
mutation serialization is outside this slice.

## Verification and remaining gates

The actual-module supplemental suite changed from 0/7 ownership cases passing on
`main` to 7/7 after the correction; all twelve integration/permission schedules
pass together. TypeScript syntax transpilation passes. Existing permission-store
tests are adjusted so authenticated fixture state is established before the
session-watching store is created. Canonical lint, typecheck, build, complete
Vitest coverage, exact-head hosted CI and independent review remain required.
No merge, release or deployment qualification is claimed here.
