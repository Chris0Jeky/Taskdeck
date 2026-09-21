# Notification store request and credential ownership

Status: draft PR #3340, 2026-09-21. Base: `307c3b8b50bec1cb0bfaea3e570a942bcb1d4451`.

## Reproduced defects

`notificationStore` previously let every inbox and preference read replace its shared surface. Confirmed mark-read and preference writes did not invalidate reads that began against an older snapshot. Inbox reads, preference reads and preference writes also assigned one loading Boolean directly, so the first settlement could clear another operation's busy state.

The first lifecycle correction treated same-user token refresh as a full data reset and could blank an unchanged inbox or detach the mounted preference form from its store value. The preservation correction then exposed a second boundary: when refresh happened during an empty initial read, the old owner was retired but the unchanged route did not remount or refetch.

## Contract

Inbox and preferences have independent latest-read owners. Each owner carries a unique token, the current lifecycle epoch and the mutation generation observed at request start. A newer request retires only the previous read in the same lane.

Successful `markAsRead` and `markAllRead` advance the inbox mutation generation and retire older inbox reads. A successful preference update does the same for preference reads. Loading is derived from active loading-owner tokens rather than whichever call settles first.

User identity, authentication or demo-session replacement advances the epoch, retires work and clears notifications and preferences.

A token-only rotation:

- preserves settled notifications and preferences;
- suppresses old-token success, failure, toast and loading settlement;
- restarts an active inbox read only while the inbox is still empty;
- restarts an active preference read only while preferences are still null;
- retains the exact inbox query captured by the active read;
- never replays mark-read, mark-all or preference-update mutations.

Mutation serialization, realtime arrival versus refresh, and reminder/email work in #2010 remain outside this slice.

## Test-first evidence

The initial supplemental actual-module suite changed from **0/10 passing on `main`** to **10/10 passing** after the first ownership correction.

Review-regression head `10d48f732725ed8ee9a2557ac39ccf7b7a7958d5` ran canonical Node 24 frontend qualification on Ubuntu and Windows. Lint, typecheck, production build and PWA validation passed on both platforms. Ubuntu JUnit recorded **7,161 tests, exactly 4 failures, 0 errors**, all loaded-state preservation cases.

Issue #3352 added test-only head `660362c9546b51f9996659be3382ac4b6d67f424`, covering token rotation while inbox and preferences are still empty. A dependency-free runner transpiled and executed the actual production module:

- before the retry correction: each read API was called once and loading became false after rotation;
- after the correction: each read API was called twice, old-token settlement was suppressed and fresh-token inbox/preferences populated independently.

Hosted exact-head qualification remains authoritative; the supplemental runner does not replace it.

## Remaining gates

Current production correction: `2192edf4e0717998b7e1c24236546902d6a9229a` before this documentation commit.

Exact final-head lint, typecheck, production build, complete Vitest on Ubuntu and Windows, Required CI, Extended, Self-Test and fresh-context review remain required. Existing notification-store, realtime, integration, demo and view suites must remain green. Stacked preference-order PR #3343 must later be reconciled to this corrected parent and requalified.

This is client-state integrity, not a server-authorization claim or transport cancellation guarantee. No merge, release or deployment qualification is claimed.
