# Connector mutation ordering

Status: stacked draft PR #3336, 2026-09-21. Parent: PR #3332 at
`ade79cd4ceac6ca3f3130ba1765039bbfbc0337a`.

## Reproduced defect

Update, delete, enable and disable started independently for one connector even
though the API accepts no expected revision and the entity has no configured
concurrency token. Same-connector commits and responses could therefore diverge
from user submission order. A queued pre-logout intent also had no transport-time
lifecycle check.

A supplemental runner executes the actual production store with only
Pinia/Vue/API/session boundaries stubbed. Against the parent, five ordering and
session schedules fail while the different-connector concurrency control passes.
The same six schedules pass after the correction.

## Contract

- One queue exists per connector ID. Update, delete, enable and disable for that
  connector run in submission order; different connectors remain concurrent.
- The first intent starts transport synchronously. Later same-connector work
  waits for its predecessor, regardless of success or failure.
- Immediately before transport, queued work rechecks the lifecycle epoch from
  submission. Session replacement clears queue registration and prevents old
  intent from using later credentials.
- A predecessor failure does not cancel the next intent. The next transport
  clears the predecessor's shared error before running.
- Existing stale-session cache, detail, toast and error settlement rules from
  #3332 remain unchanged.
- Delete remains ordered rather than magical: later intent still reaches the
  server and may receive NotFound; no client resurrection is introduced.

The client queue preserves one client's submission order only. It does not solve
cross-device concurrency; the backend currently exposes no revision precondition.

## Verification and remaining gates

Actual-module red/green: 1/6 schedules passed on the parent, 6/6 after correction.
The preceding seventeen read, permission and session ownership schedules remain
17/17 green. Changed TypeScript source/tests transpile without diagnostics.
Canonical Pinia/Vitest, lint, project typecheck, build, full hosted CI and
independent review are still required. Because this is a third-level stack,
retarget only after #3329 and #3332 land, verify the child-only diff and requalify
against current `main`. No merge, release or deployment qualification is claimed.
