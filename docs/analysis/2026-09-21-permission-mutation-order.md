# Board-access mutation ordering

Status: integration of source PR #3335 after parent PR #3330 merged, 2026-09-22.

## Reproduced defects

`updateAccess` and `revokeAccess` started independently for one access row even though the API accepts no expected revision and the entity has no configured concurrency token. Two role changes could therefore commit or settle in an order that differed from the user's clicks. Update/revoke could also overlap, and a queued pre-logout intent had no transport-time session check.

Independent mutation failures exposed a second ownership gap during review. A queued same-entry mutation cleared the store's shared error when its transport started. If another access row failed while the queued intent was waiting, the queued start erased that unrelated receipt.

## Contract

- One queue exists per `{boardId, accessId}`. Update and revoke for that row run in submission order; different rows remain concurrent.
- The first intent starts transport synchronously. A later intent waits for its predecessor to finish, regardless of success or failure.
- Each queued operation owns a loading token from submission through settlement, so loading does not drop between same-entry operations.
- Immediately before transport, queued work rechecks the initiating session epoch. Identity, token, authentication or demo replacement clears queue registration and prevents old intent from using later credentials.
- A predecessor failure does not cancel the next intent. The queued transport clears an error only when that receipt is owned by its own predecessor; an unrelated concurrent failure remains visible.
- Existing successful-mutation read invalidation, stable-ID grant deduplication and stale settlement rules from #3330 remain unchanged.

The client queue preserves one client's submission order only. It does not solve cross-device concurrency; the backend currently exposes no revision precondition.

## Evidence and remaining gates

The initial ordering negative control ran the actual production store with only framework/API/session boundaries stubbed: one independent-row control passed and four ordering/session schedules failed on the parent, then all five passed after serialization.

Codex review identified the independent-error case. A dedicated deferred Pinia regression was committed before the correction: access row 2 fails while row 1's second update waits, then row 1's queued transport starts without erasing row 2's error.

The integration is rebuilt on the parent's token-rotation correction. A revoke-then-update regression also proves the second transport waits and a failed update does not resurrect the revoked row. Local focused permission tests pass (45 before the final added case, 46 after it); the full frontend suite passes 520 files, 7,387 tests with three existing skips. Typecheck, build, lint, docs governance and doc links pass; lint reports 11 existing warnings. The source commits, child-only diff, exact-head hosted CI and independent review must be verified before merge. No release or deployment qualification is claimed.
