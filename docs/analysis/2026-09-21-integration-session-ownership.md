# Integration session and mutation ownership

Status: stacked draft PR #3332, 2026-09-21. Parent: PR #3329 at
`4e232e22778489681b9db5746e5c221ada7a088a`.

## Reproduced defects

The integration store was not connected to session lifecycle. Loaded connectors
survived logout, and register/update/delete/enable/disable settlements retained
permission to patch cache, detail, error and toast after reset or account
replacement. Same-ID state installed for a later account could consequently be
overwritten or removed by an older mutation response.

The committed Pinia suite covers cache clearing, same-user logout/login, stale
register, same-ID update/delete and stale failure. A supplemental runner executes
the actual production store with only framework/API/session boundaries stubbed:
all five child schedules fail against the #3329 parent and pass after correction.
All seventeen read, permission and session/mutation ownership schedules pass
together on the corrected local source.

## Contract

- The store watches session identity/auth/demo transitions synchronously and uses
  the existing `$reset()` boundary to advance one lifecycle epoch.
- Reads and mutations capture the same lifecycle epoch.
- A stale mutation may still resolve or reject to its initiating caller, but it
  cannot patch list/detail state or publish error/success UI in the replacement
  session.
- Logout/login as the same user still crosses a null identity and invalidates the
  earlier lifetime.
- Server effects accepted before logout are not presented as cancelled or rolled
  back.

The API, DTOs and backend are unchanged. Concurrent mutation ordering within one
live session is not solved here and remains an explicit review boundary.

## Verification and remaining gates

Actual-module red/green: 0/5 child schedules on the parent, 5/5 after correction;
17/17 combined schedules pass after correction. Changed TypeScript source/tests
transpile without diagnostics. Canonical Pinia/Vitest, lint, project typecheck,
build, full hosted matrix and independent review remain required. Because this
is stacked work, it must be retargeted to current `main` and requalified after
#3329 lands. No merge, release or deployment qualification is claimed.
