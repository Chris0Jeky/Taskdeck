# Activity history request ownership

Status: corrective draft for #3344 / PR #3345, based on `main`
`307c3b8b50bec1cb0bfaea3e570a942bcb1d4451`.

## Reproduced defect

Board, entity, and user history requests all replace one `auditStore.entries`
surface. The previous implementation allowed every response, failure, toast, and
`finally` to commit. Ordinary Activity route changes could therefore restore an
older query, report an obsolete failure, or clear loading while the current query
was still pending. The store was also absent from AppShell's logout resets, so a
request started with retired credentials could repopulate audit data later.

## Contract

- The shared result surface has one current request owner across all query kinds.
- A newer query retires the previous owner's permission to commit UI state.
- Identity, token, authentication, or demo-session replacement synchronously
  advances the credential epoch and clears the audit surface.
- A stale request still resolves or rejects to its original caller, but cannot
  write entries, errors, toasts, loading, or final state.
- A current failure retains the previous result list and preserves the existing
  public error/toast/rejection behavior.
- Limit clamping, endpoints, route behavior, demo behavior, and the public store
  API remain unchanged.

The three duplicated request bodies now use one private request helper so their
ownership and failure rules cannot drift independently. This is client-state
integrity, not transport cancellation or a server authorization claim.

## Evidence and remaining gates

The committed real Pinia/Vitest suite covers five deferred schedules. A bounded
supplemental runner transpiles and executes the actual production store with only
framework/API/session boundaries stubbed:

- unchanged `main`: 0/5 passed;
- corrected source: 5/5 passed.

The supplemental runner is not committed and does not replace canonical frontend
qualification. Before review-ready status, inspect the test-only hosted RED
artifact, then require exact-head lint, typecheck, production build, full Vitest
on Ubuntu and Windows, the complete Required CI/Extended/Self-Test workflows, and
fresh-context review. No merge, release, or deployment qualification is claimed.
