# Notification store request and credential ownership

Status: draft PR #3340, 2026-09-21. Base: `307c3b8b50bec1cb0bfaea3e570a942bcb1d4451`.

## Reproduced defects

`notificationStore` previously let every inbox and preference read replace its shared surface. Confirmed mark-read and preference writes did not invalidate reads that began against an older snapshot. Inbox reads, preference reads and preference writes also assigned one loading Boolean directly, so the first settlement could clear another operation's busy state.

The store had no credential lifetime. Work started under one account or token could settle after logout, login or refresh and populate the replacement notification surfaces or emit stale error/toast UI.

## Contract

Inbox and preferences have independent latest-read owners. Each owner carries a unique token, the current credential epoch and the mutation generation observed at request start. A newer request retires only the previous read in the same lane.

Successful `markAsRead` and `markAllRead` advance the inbox mutation generation and retire an older inbox read. A successful preference update does the same for preference reads. Current loading is derived from active loading-owner tokens rather than whichever call settles first.

The store watches user identity, token, authenticated state and demo state synchronously. Any replacement advances the epoch, retires owners and clears notifications, preferences, error and loading. Stale work still resolves or rejects to its original caller but cannot patch replacement state, toast or clear current loading.

Mutation serialization, realtime arrival versus refresh, and the reminder/email feature work in #2010 remain outside this slice.

## Test-first evidence

The committed test-only head is `8001750590dfa971aad87c2a82d35558abef487d`. Its hosted workflows were still queued when the production correction was published and may be superseded; no canonical RED result is claimed unless a completed artifact is later inspected.

A dependency-free supplemental runner transpiled and executed the actual production module with only its Pinia/Vue/API/session boundaries stubbed. Against unchanged `main`, all ten ownership schedules failed (**0/10 passed**). Against the correction, all ten passed (**10/10**).

The schedules cover reverse inbox and preference reads; stale reads after mark-one, mark-all and preference writes; independent loading ownership; token-rotation clearing; stale read failure; stale mark success; and stale preference-write failure.

## Verification and remaining gates

The corrected production module transpiles under TypeScript 5.8.3 with zero diagnostics. Canonical Pinia/Vitest, lint, project typecheck, build, exact-head hosted CI and independent review are still required. Existing notification-store, realtime, integration, demo and view suites must remain green.

This is client-state integrity, not a server-authorization claim or transport cancellation guarantee. No merge, release or deployment qualification is claimed by this note.
