# Notification store request and credential ownership

Status: draft PR #3340, 2026-09-21. Base: `307c3b8b50bec1cb0bfaea3e570a942bcb1d4451`.

## Reproduced defects

`notificationStore` previously let every inbox and preference read replace its shared surface. Confirmed mark-read and preference writes did not invalidate reads that began against an older snapshot. Inbox reads, preference reads, and preference writes also assigned one loading Boolean directly, so the first settlement could clear another operation's busy state.

The initial lifecycle correction invalidated old work, but treated a same-user token refresh as a full data reset. A successful session extension could therefore blank an unchanged inbox route or detach the mounted preference form from its loaded store value.

## Contract

Inbox and preferences have independent latest-read owners. Each owner carries a unique token, the current lifecycle epoch, and the mutation generation observed at request start. A newer request retires only the previous read in the same lane.

Successful `markAsRead` and `markAllRead` advance the inbox mutation generation and retire older inbox reads. A successful preference update does the same for preference reads. Loading is derived from active loading-owner tokens rather than whichever call settles first.

User identity, authentication, or demo-session replacement advances the epoch, retires work, and clears notifications and preferences. Token-only rotation advances the same operation epoch and clears transient loading/error ownership, but preserves loaded notifications and preferences for the unchanged user and route. Stale work still resolves or rejects to its original caller but cannot patch state, toast, or clear current loading.

Mutation serialization, realtime arrival versus refresh, and reminder/email work in #2010 remain outside this slice.

## Test-first evidence

The initial supplemental actual-module suite changed from **0/10 passing on `main`** to **10/10 passing** after the first ownership correction.

Review-regression head `10d48f732725ed8ee9a2557ac39ccf7b7a7958d5` ran canonical Node 24 frontend qualification on Ubuntu and Windows. Lint, typecheck, production build, and PWA validation passed on both platforms. Ubuntu JUnit recorded **7,161 tests, exactly 4 failures, 0 errors**; every failure was a new token-refresh preservation case:

1. preserve inbox and preferences while old reads settle;
2. preserve inbox while an old read fails;
3. preserve inbox while an old mark-read succeeds;
4. preserve preferences while an old save fails.

No unrelated frontend test failed.

## Remaining gates

The production correction splits token-only operation invalidation from full identity reset. Exact-head canonical tests, complete Required CI, Extended, Self-Test, and fresh-context review remain required. Existing notification-store, realtime, integration, demo, and view suites must remain green.

This is client-state integrity, not a server-authorization claim or transport cancellation guarantee. No merge, release, or deployment qualification is claimed.
