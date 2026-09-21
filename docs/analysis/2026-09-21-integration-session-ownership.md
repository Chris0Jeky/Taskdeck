# Integration session and mutation ownership

Status: stacked draft PR #3332, 2026-09-21. Parent: PR #3329 at `4e232e22778489681b9db5746e5c221ada7a088a`.

## Reproduced defects

The integration store was not connected to session lifecycle. Loaded connectors survived logout, and register/update/delete/enable/disable settlements retained permission to patch cache, detail, error, and toast after account replacement. Same-ID state installed for a later account could consequently be overwritten or removed by an older mutation response.

The initial lifecycle correction invalidated old work but treated every token refresh as a full store reset. A successful same-user session extension could therefore blank the mounted Integrations route even though its user and selected connector were unchanged.

## Contract

- Reads and mutations capture one shared lifecycle epoch.
- User identity, authentication, or demo-session replacement advances that epoch through `$reset()`, retires work, and clears connector list/detail state.
- Token-only rotation advances the operation epoch and clears transient read/loading/error ownership, but preserves loaded connector list/detail state for the unchanged user and route.
- A stale read or mutation may still resolve or reject to its initiating caller, but cannot patch current list/detail state or publish stale error/success UI.
- Logout/login as the same user crosses a null identity and invalidates the earlier lifetime.
- Server effects accepted before logout are not represented as cancelled or rolled back.

The API, DTOs, and backend are unchanged. Concurrent same-connector mutation ordering remains in stacked PR #3336.

## Test-first evidence

The original child suite covers cache clearing on logout, same-user relogin, stale register, same-ID update/delete, and stale mutation failure. A supplemental actual-module runner changed from **0/5 passing on the parent** to **5/5 passing** after the initial lifecycle correction.

Review-regression head `9934e9f029e8942575040f07a76f7b42e0946f37` ran canonical Node 24 frontend qualification on Ubuntu and Windows. Lint, typecheck, production build, and PWA validation passed on both platforms. Ubuntu JUnit recorded **7,163 tests, exactly 2 failures, 0 errors**; both failures were the new token-refresh preservation schedules:

1. preserve loaded connectors while suppressing an old-token list read;
2. preserve loaded connectors while suppressing an old-token mutation failure.

No unrelated frontend test failed.

## Remaining gates

The production correction separates token-only invalidation from full identity reset. Exact-head canonical tests, complete Required CI, Extended, Self-Test, and fresh review remain required. Because this is stacked work, it must be reconciled and requalified after #3329 lands; #3336 must then be reconciled to this corrected parent.

No merge, release, or deployment qualification is claimed.
