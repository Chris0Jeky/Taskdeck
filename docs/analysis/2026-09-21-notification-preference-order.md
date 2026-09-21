# Notification preference mutation ordering

Status: stacked draft PR #3343, 2026-09-21. Parent: PR #3340.

## Reproduced defect

Every `updatePreferences` call previously started transport immediately. The API carries no expected revision and the persisted notification-preference row has no configured concurrency token. Two saves from one client could therefore commit or settle in an order different from the user’s submissions.

A queued correction also needs explicit error ownership. An independent inbox read may fail while the next preference save waits; starting that queued save must not erase the unrelated inbox receipt.

## Contract

- One preference-mutation lane preserves this client’s submission order.
- The first save starts transport synchronously. Later saves wait for their predecessor, regardless of success or failure.
- Every queued save owns a loading token from submission through settlement.
- Immediately before transport, queued work rechecks the credential epoch inherited from #3340. Token, identity, authentication or demo replacement clears queue registration and prevents old intent from running with later credentials.
- Error receipts carry the operation token that produced them. Queued start retires only its own predecessor’s receipt; an independent inbox failure remains visible.
- Successful saves retain #3340’s preference-read invalidation.

This preserves one client’s order only. It does not claim cross-device concurrency safety or add a server-side revision precondition.

## Test-first evidence

Test-only child head: `4f89c6f2969d5dbad923841b4984ef152daef824`.

A supplemental runner transpiled and executed the actual parent and corrected production stores with framework/API/session boundaries stubbed:

- parent #3340: **1/5 passed**; only the existing loading-token control passed;
- corrected child: **5/5 passed**.

The four parent failures demonstrated eager second transport, failed-predecessor overlap, queued old-credential transport, and lack of a real queued boundary for independent-error preservation.

The committed Pinia suite covers immediate first transport, serialization, failed-predecessor continuation, token replacement, queued loading, and preservation of an independent inbox failure.

## Verification and remaining gates

The corrected module transpiles under TypeScript 5.8.3 with zero diagnostics. Exact-head Pinia/Vitest, lint, project typecheck, build, full hosted CI and independent review remain required. After #3340 lands, retarget to current `main`, verify the child-only diff and requalify.

No merge, release or deployment qualification is claimed by this note.
