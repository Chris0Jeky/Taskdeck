# Integration session and mutation ownership

Status: stacked draft PR #3332, 2026-09-21. Parent: PR #3329 at `4e232e22778489681b9db5746e5c221ada7a088a`.

## Reproduced defects

The integration store was not connected to session lifecycle. Loaded connectors survived logout, and register/update/delete/enable/disable settlements retained permission to patch cache, detail, error and toast after account replacement. Same-ID state installed for a later account could consequently be overwritten or removed by an older mutation response.

The first lifecycle correction treated every token refresh as full store reset and could blank a mounted Integrations route. The preservation correction then exposed a second boundary: an empty first list/detail read was retired during refresh and the unchanged route did not refetch.

## Contract

- Reads and mutations capture one shared lifecycle epoch.
- User identity, authentication or demo-session replacement advances that epoch through `$reset()`, retires work and clears connector list/detail state.
- Token-only rotation preserves settled list/detail data, suppresses old-token UI settlement and restarts only active read lanes whose visible surface is still empty.
- The detail retry retains the exact connector ID captured by the active read.
- A stale read or mutation may still resolve or reject to its initiating caller, but cannot patch current list/detail state or publish stale error/success UI.
- Register, update, delete, enable and disable mutations are never replayed.
- Logout/login as the same user crosses a null identity and invalidates the earlier lifetime.
- Server effects accepted before logout are not represented as cancelled or rolled back.

The API, DTOs and backend are unchanged. Concurrent same-connector mutation ordering remains in stacked PR #3336.

## Test-first evidence

The original child suite covers cache clearing on logout, same-user relogin, stale register, same-ID update/delete and stale mutation failure. A supplemental actual-module runner changed from **0/5 passing on the parent** to **5/5 passing** after the initial lifecycle correction.

Review-regression head `9934e9f029e8942575040f07a76f7b42e0946f37` ran canonical Node 24 frontend qualification on Ubuntu and Windows. Lint, typecheck, production build and PWA validation passed on both platforms. Ubuntu JUnit recorded **7,163 tests, exactly 2 failures, 0 errors**, both loaded-cache preservation schedules.

Issue #3352 added test-only head `03f46771c7e4e128542331ba1aba2a5089b3a023`, covering token rotation while connector list and detail are still empty. A dependency-free runner transpiled and executed the actual production module:

- before the retry correction: list/detail APIs were called once and loading became false after rotation;
- after the correction: both APIs were called twice, the same detail ID was retained, old-token settlement was suppressed and fresh-token list/detail populated independently.

Hosted exact-head qualification remains authoritative; the supplemental runner does not replace it.

## Remaining gates

Current production correction: `0dbf40b8337e3cbbee1d6782e4cd100f3314ee1f` before this documentation commit.

Exact final-head lint, typecheck, production build, complete Vitest on Ubuntu and Windows, Required CI, Extended, Self-Test and fresh review remain required. Because this is stacked work, it must be reconciled and requalified after #3329 lands; #3336 must then be reconciled to this corrected parent.

No merge, release or deployment qualification is claimed.
