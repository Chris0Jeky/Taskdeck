# Board-access read and session ownership

Status: draft PR #3330, 2026-09-21. Base: `307c3b8b50bec1cb0bfaea3e570a942bcb1d4451`.

## Reproduced defects

A board-access read could settle after a confirmed grant, update or revoke and replace newer client state. Same-board reads were last-response-wins, one shared loading Boolean could clear while other boards still loaded, and pending reads or mutations retained permission to publish after account replacement.

The first lifecycle correction treated every token refresh as a full cache reset and could blank an unchanged Board Access route. The preservation correction then exposed a second boundary: when refresh happened during an unresolved first read, the old owner was retired but the unchanged board route did not refetch.

## Contract

- One read owner exists per board; unrelated boards remain concurrent.
- A successful mutation advances that board's generation and retires older reads.
- User identity, authentication or demo-session replacement advances the epoch, clears cached access, retires operations and resets loading/error.
- Token-only rotation preserves settled board caches, suppresses old-token UI settlement and restarts only active board reads that do not yet have a cache entry.
- An empty array is a settled authoritative cache and is not retried merely because it is empty.
- The retry retains the exact board ID captured by the active read.
- Success, failure, toast and cache writes require the initiating lifecycle epoch.
- Loading is derived from current operation tokens, not whichever call settles.
- Grant, update and revoke mutations are never replayed.

Server authorization remains authoritative. This corrects truthful client cache behavior and does not claim a server-side authorization bypass. Same-entry mutation serialization remains in stacked PR #3335.

## Test-first evidence

The original ownership suite covers read-versus-grant/update/revoke races, reverse reads, independent loading, same-user logout/login, replacement-session mutation settlement, stale failures and stable-ID grant deduplication.

Review-regression head `6bbf8d04139bef2dca91d290b910e1c07a8e76aa` ran canonical Node 24 frontend qualification on Ubuntu and Windows. Lint, typecheck, production build and PWA validation passed on both platforms. Ubuntu JUnit recorded **7,161 tests, exactly 2 failures, 0 errors**, both loaded-cache preservation cases.

Issue #3352 added test-only head `d28697ede737ec42ae3e696c7a7cedcb753de347`, covering token rotation while `board-1` has an unresolved first read and no cache entry. A dependency-free runner transpiled and executed the actual production module:

- before the retry correction: `getAccess` was called once and loading became false after rotation;
- after the correction: `getAccess` was called twice for the same board, old-token settlement was suppressed and the fresh-token result populated the cache.

Hosted exact-head qualification remains authoritative; the supplemental runner does not replace it.

## Remaining gates

Current production correction: `3c7746ee0330406a4e6b84c1a5bc253b02a7aa08` before this documentation commit.

Exact final-head lint, typecheck, production build, complete Vitest on Ubuntu and Windows, Required CI, Extended, Self-Test and repeat independent review remain required. Stacked mutation-order PR #3335 must later be reconciled to this corrected parent and requalified.

No merge, release or deployment qualification is claimed.
