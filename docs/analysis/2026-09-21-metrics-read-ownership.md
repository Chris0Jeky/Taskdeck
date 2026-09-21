# Metrics and forecast request ownership

Status: corrective draft for #3346 / PR #3347, based on `main`
`307c3b8b50bec1cb0bfaea3e570a942bcb1d4451`.

## Reproduced defect

Board metrics and forecast requests own separate visible surfaces, but each method
previously committed every response, failure, toast and `finally`. A board/date
selection change could therefore restore an older result or clear the current
lane's loading state. `$reset()` cleared refs without invalidating requests
already in flight, and the store had no same-user token/session replacement
boundary.

A later ownership review found that treating a token-only refresh as a complete
reset creates a separate false-empty state. `MetricsView` fetches when the board
or range changes, so session extension on an unchanged route cleared the current
dashboard without triggering another read.

## Contract

- Metrics and forecast retain independent latest-request owners.
- A newer request retires only the previous owner in the same lane.
- `$reset()` and user-identity, authentication or demo-session replacement
  synchronously advance one credential epoch and clear both data surfaces.
- A token-only rotation advances that same request epoch and clears transient
  loading/errors, but preserves already loaded metrics and forecast for the
  unchanged user, board and route.
- Stale requests still resolve or reject to their original callers, but cannot
  write results, errors, toasts, loading or final state.
- A current failure preserves the previous result and the existing public
  error/toast/rejection behavior.
- Metrics completion cannot clear forecast loading, and forecast completion
  cannot clear metrics loading.
- Demo messages, endpoints, query types and the public store API remain unchanged.

This is client-state integrity. It does not cancel transport or change server
metrics authorization.

## Evidence and remaining gates

The initial committed real Pinia/Vitest suite covers seven deferred schedules. A
bounded supplemental runner transpiles and executes the actual store with only
framework/API/session boundaries stubbed:

- unchanged `main`: 1/7 passed, with only the independent-lane control green;
- initial corrected source: 7/7 passed.

Review-regression head `f9f6bc9479ec7d211077b545be95a64cf63e65ae`
changed the token-rotation contract from clearing to preserving loaded dashboard
data. Ubuntu passed lint, typecheck, build and PWA validation; its JUnit artifact
ran all seven ownership cases and failed only
`preserves loaded dashboard data while invalidating old-token work on refresh`,
with the current metrics value cleared instead of retaining board `existing`.

The supplemental runner is not committed and does not replace project
qualification. The current production correction requires exact-head lint,
typecheck, production build, full Vitest on Ubuntu and Windows, complete Required
CI/Extended/Self-Test workflows, and a fresh-context review. No merge, release or
deployment qualification is claimed.
