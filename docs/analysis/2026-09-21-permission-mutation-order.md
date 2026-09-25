# Board-access mutation ordering

Status: integration of source PR #3335 after parent PR #3330 merged, 2026-09-22.

## Reproduced defects

`updateAccess` and `revokeAccess` started independently for one access row even though the API accepts no expected revision and the entity has no configured concurrency token. Two role changes could therefore commit or settle in an order that differed from the user's clicks. Update/revoke could also overlap, and a queued pre-logout intent had no transport-time session check.

Independent mutation failures exposed a second ownership gap during review. A queued same-entry mutation cleared the store's shared error when its transport started. If another access row failed while the queued intent was waiting, the queued start erased that unrelated receipt.

## Contract

- One queue exists per `{boardId, accessId}`. Update and revoke for that row run in submission order; different rows remain concurrent.
- The first intent starts transport synchronously. A later intent waits for its predecessor to finish, regardless of success or failure.
- Each queued operation owns a loading token from submission through settlement, so loading does not drop between same-entry operations.
- Immediately before transport, queued work rechecks the initiating session epoch. Identity, token, authentication or demo replacement retires old queued intent without using later credentials. In-flight write tails remain registered until settlement so a new session's same-entry intent cannot overtake an older server write.
- A predecessor failure does not cancel the next intent. The queued transport clears an error only when that receipt is owned by its own predecessor; an unrelated concurrent failure remains visible.
- Existing successful-mutation read invalidation, stable-ID grant deduplication and stale settlement rules from #3330 remain unchanged.

The client queue preserves one client's submission order only. It does not solve cross-device concurrency; the backend currently exposes no revision precondition.

## Evidence and remaining gates

The initial ordering negative control ran the actual production store with only framework/API/session boundaries stubbed: one independent-row control passed and four ordering/session schedules failed on the parent, then all five passed after serialization.

Codex review identified the independent-error case. A dedicated deferred Pinia regression was committed before the correction: access row 2 fails while row 1's second update waits, then row 1's queued transport starts without erasing row 2's error.

The integration is rebuilt on the parent's token-rotation correction. A revoke-then-update regression proves the second transport waits and a failed update does not resurrect the revoked row. Independent review found that clearing lane tails on token rotation let a new intent race an old in-flight write. A deferred regression failed on that reviewed head and passes after retaining the tail. The final focused permission run passes 47 tests in five files. The earlier full frontend run passed 520 files, 7,387 tests with three existing skips; final-head hosted CI remains required. Typecheck and changed-file lint pass at the fix, while full build/lint passed before it with 11 existing warnings. Docs governance and doc links pass after the fix. The source commits and child-only diff are verified, while exact-head hosted CI and fix review remain required before merge. A never-settling transport can hold its lane indefinitely; that lower-severity risk is tracked in #3364. No release or deployment qualification is claimed.
