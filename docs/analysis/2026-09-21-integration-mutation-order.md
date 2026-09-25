# Connector mutation ordering

Status: stacked draft PR #3336, 2026-09-21. Parent: PR #3332, itself stacked on #3329.

## Reproduced defects

Update, delete, enable and disable started independently for one connector even though the API accepts no expected revision and the entity has no configured concurrency token. Same-connector commits and responses could therefore diverge from user submission order. A queued pre-logout intent also had no transport-time lifecycle check.

Independent mutation failures exposed a second ownership gap during review. A queued same-connector mutation cleared the store's shared error when its transport started. If another connector failed while the queued intent was waiting, the queued start erased that unrelated receipt.

## Contract

- One queue exists per connector ID. Update, delete, enable and disable for that connector run in submission order; different connectors remain concurrent.
- The first intent starts transport synchronously. Later same-connector work waits for its predecessor, regardless of success or failure.
- Immediately before transport, queued work rechecks the lifecycle epoch from submission. Identity, token, authentication or demo replacement clears queue registration and prevents old intent from using later credentials.
- A predecessor failure does not cancel the next intent. The queued transport clears an error only when that receipt is owned by its own predecessor; an unrelated concurrent connector failure remains visible.
- Existing stale-session cache, detail, toast and error settlement rules from #3332 remain unchanged.
- Delete remains ordered rather than magical: later intent still reaches the server and may receive NotFound; no client resurrection is introduced.

The client queue preserves one client's submission order only. It does not solve cross-device concurrency; the backend currently exposes no revision precondition.

## Evidence and remaining gates

The initial ordering negative control executed the actual production store with only framework/API/session boundaries stubbed: one independent-connector control passed and five ordering/session schedules failed on the parent, then all six passed after serialization.

Codex review identified the independent-error case. A dedicated deferred Pinia regression was committed before the correction: connector 2 fails while connector 1's second update waits, then connector 1's queued transport starts without erasing connector 2's error.

This child is rebuilt on the parent token-rotation correction rather than retaining its older copy of the integration store. Exact-head Pinia/Vitest, lint, project typecheck, build, the complete hosted matrix and repeat independent review remain required. Because this is a third-level stack, retarget only after #3329 and #3332 land, verify the child-only diff and requalify against current `main`. No merge, release or deployment qualification is claimed.
