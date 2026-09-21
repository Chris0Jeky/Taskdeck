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

## Contract

- Metrics and forecast retain independent latest-request owners.
- A newer request retires only the previous owner in the same lane.
- `$reset()` and identity, token, authentication or demo-session replacement
  synchronously advance one credential epoch and clear both lanes.
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

The committed real Pinia/Vitest suite covers seven deferred schedules. A bounded
supplemental runner transpiles and executes the actual store with only
framework/API/session boundaries stubbed:

- unchanged `main`: 1/7 passed, with only the independent-lane control green;
- corrected source: 7/7 passed.

The supplemental runner is not committed and does not replace project
qualification. Before review-ready status, inspect the test-only hosted RED
artifact, then require exact-head lint, typecheck, production build, full Vitest
on Ubuntu and Windows, complete Required CI/Extended/Self-Test workflows, and a
fresh-context review. No merge, release or deployment qualification is claimed.
