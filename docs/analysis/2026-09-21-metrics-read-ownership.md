# Metrics and forecast request ownership

Status: corrective draft for #3346 / PR #3347. Base: `307c3b8b50bec1cb0bfaea3e570a942bcb1d4451`.

## Reproduced defects

Board metrics and forecast own separate visible surfaces, but the original store committed every response, failure, toast and `finally`. Board/date changes could restore older results or clear current loading, `$reset()` did not invalidate in-flight work, and the store had no session boundary.

Review then exposed two token-refresh defects:

1. full reset on same-user refresh cleared already loaded dashboard data;
2. preservation alone stranded an empty first load because the old request was retired and the unchanged route did not refetch.

## Contract

- Metrics and forecast retain independent latest-request owners.
- A newer request retires only the previous owner in the same lane.
- User identity, authentication or demo-session replacement advances the epoch and clears both data surfaces.
- Token-only rotation preserves settled data, retires old-token UI settlement, and restarts only an active lane whose visible surface is still null.
- Retried metrics/forecast reads retain the exact query captured by the active request.
- Stale requests still resolve or reject to their original callers, but cannot write results, errors, toasts, loading or final state.
- A current failure preserves the previous result and the public error/toast/rejection behavior.
- Metrics and forecast loading remain independent.
- No mutation is replayed and no endpoint, query type or public store API changes.

This is client-state integrity, not transport cancellation or a server metrics-authorization change.

## Test-first evidence

The initial real Pinia suite covered seven deferred schedules. A bounded actual-module runner changed from **1/7 passing on `main`** to **7/7 passing** after the first correction.

Review-regression head `f9f6bc9479ec7d211077b545be95a64cf63e65ae` isolated loaded-dashboard preservation. The corrected head `8f31b2b72e6941b5e77ab730aea34da8da75e9af` passed Smart CI, Extended and the complete Required CI matrix.

Issue #3352 then added test-only head `b7425560e7f3c90833dde8bc74d82543d39ff389`, covering a token rotation while both metrics and forecast are still null. A dependency-free runner transpiled and executed the actual production module:

- before the retry correction: each API was called once and both loading flags became false;
- after the correction: each API was called twice, old-token settlement was suppressed, and fresh-token results populated both lanes.

Hosted exact-head qualification remains authoritative; the supplemental runner does not replace it.

## Remaining gates

Current production correction: `8566adbabd9abe5ddca9a5b09b79a928616db10f` before this documentation commit.

Exact final-head lint, typecheck, production build, complete Vitest on Ubuntu and Windows, Required CI, Extended, Self-Test and fresh-context review remain required. Review should focus on query capture, no retry loops, and no mutation replay.

No merge, release or deployment qualification is claimed.
