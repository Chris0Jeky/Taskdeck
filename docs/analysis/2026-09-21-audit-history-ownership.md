# Activity history request ownership

Status: corrective draft for #3344 / PR #3345. Base: `307c3b8b50bec1cb0bfaea3e570a942bcb1d4451`.

## Reproduced defect

Board, entity, and user history requests replace one `auditStore.entries` surface. The original store allowed every response, failure, toast, and `finally` to commit, so ordinary Activity route changes could restore an older query, report an obsolete failure, or clear loading while the current query was pending.

The initial lifecycle correction then treated every token rotation as a full data reset. A successful same-user session extension therefore cleared the current Activity result even though the route and selected query were unchanged.

## Contract

- One current request owner exists across board, entity, and user query kinds.
- A newer query retires the previous owner's permission to commit UI state.
- User identity, authentication, or demo-session replacement advances the epoch, retires work, and clears history.
- Token-only rotation advances the same request epoch and clears transient loading/error ownership, but preserves the already loaded history for the unchanged user and route.
- Stale work still resolves or rejects to its original caller, but cannot write entries, error, toast, loading, or final state.
- A current failure retains the previous result list and preserves the public error/toast/rejection behavior.
- Limit clamping, endpoints, route behavior, demo behavior, and the public store API remain unchanged.

The three request bodies use one private helper so ownership and failure rules cannot drift. This is client-state integrity, not transport cancellation or a server authorization claim.

## Test-first evidence

The initial test-only head `3980e1e3e234a251cd89cad270b8d0ab86c3e5f2` produced five intended ownership failures against unchanged `main`.

Review-regression head `93c20b679888394200354d80040e7f3c7dd5c353` ran the canonical Node 24 frontend suite on Ubuntu and Windows. Lint, typecheck, production build, and PWA validation passed on both platforms. Ubuntu JUnit recorded **7,156 tests, exactly 2 failures, 0 errors**; both failures were the new token-refresh preservation cases:

1. preserve loaded history while suppressing an old-token success;
2. preserve loaded history while suppressing an old-token failure.

No unrelated frontend test failed.

## Remaining gates

The production correction splits token-only invalidation from full identity reset. Exact-head lint, typecheck, build, complete Vitest on Ubuntu and Windows, full Required CI, Extended, Self-Test, and fresh-context review are still required. No merge, release, or deployment qualification is claimed.
