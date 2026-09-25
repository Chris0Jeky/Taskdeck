# Activity history request ownership

Status: corrective draft for #3344 / PR #3345. Base: `307c3b8b50bec1cb0bfaea3e570a942bcb1d4451`.

## Reproduced defects

Board, entity and user history requests replace one `auditStore.entries` surface. The original store allowed every response, failure, toast and `finally` to commit, so route changes could restore older queries, report obsolete failures or clear current loading.

The first lifecycle correction treated token refresh as full identity reset and cleared loaded history. The preservation correction then exposed a second boundary: an empty initial history read was retired during refresh and the unchanged route did not refetch.

## Contract

- One current request owner exists across board, entity and user query kinds.
- A newer query retires the previous owner's permission to commit UI state.
- User identity, authentication or demo-session replacement advances the epoch, retires work and clears history.
- Token-only rotation preserves settled history, suppresses old-token UI settlement and restarts the active query only while history is still empty.
- The retry retains the exact board/entity/user parameters and limit captured by the active query.
- Stale work still resolves or rejects to its original caller, but cannot write entries, error, toast, loading or final state.
- A current failure retains the previous result list and preserves the public error/toast/rejection behavior.
- Limit clamping, endpoints, route behavior, demo behavior and the public store API remain unchanged.

The three request bodies use one private helper so ownership and failure rules cannot drift. This is client-state integrity, not transport cancellation or a server authorization claim.

## Test-first evidence

Initial test-only head `3980e1e3e234a251cd89cad270b8d0ab86c3e5f2` produced five intended ownership failures against unchanged `main`.

Review-regression head `93c20b679888394200354d80040e7f3c7dd5c353` ran the canonical Node 24 frontend suite on Ubuntu and Windows. Lint, typecheck, production build and PWA validation passed on both platforms. Ubuntu JUnit recorded **7,156 tests, exactly 2 failures, 0 errors**, both loaded-history preservation cases.

Issue #3352 added test-only head `85cf369ddd11dfe0a91052eb1523efbddf904a7c`, covering token rotation during an empty initial board-history read. A dependency-free runner transpiled and executed the actual production module:

- before the retry correction: the API was called once and loading became false after rotation;
- after the correction: the API was called twice, old-token settlement was suppressed and the fresh-token result populated history.

Hosted exact-head qualification remains authoritative; the supplemental runner does not replace it.

## Remaining gates

Current production correction: `110edf3029450dc261b4feee30d6c78dd6e00405` before this documentation commit.

Exact final-head lint, typecheck, production build, complete Vitest on Ubuntu and Windows, Required CI, Extended, Self-Test and fresh-context review remain required. Review should focus on exact query capture, no retry after route clear and no retry loops.

No merge, release or deployment qualification is claimed.
