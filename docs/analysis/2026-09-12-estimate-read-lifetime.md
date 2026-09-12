# Estimate read lifetime and recovery (GH-3063)

Last Updated: 2026-09-12

Status: implementation candidate. No local Vue/Vitest, typecheck, build or browser
qualification is claimed; exact-head hosted qualification remains required.

## Problem and ownership

An accepted connection that never replies left Estimates loading and its Refresh
control disabled indefinitely. Generation checks protected against late results but
did not release network resources. Closing/reopening could accumulate pending reads.

The repair stays in the existing API wrapper and composable; it adds no global
request manager, cache, dependency, endpoint, polling loop or board mutation.
Both Paper and Legacy already consume this shared panel. Capture/review trust is
unchanged: the change reduces recovery friction for a read-only summary, never
turns a displayed aggregate into permission or approval evidence.

## Two bounds, one owner

`estimateRollupsApi.get` uses the shared HTTP client, its existing 10-second board
request timeout, `skipRetry: true`, and a caller-owned AbortSignal. Authentication,
request IDs and error mapping still use the common boundary. The timeout cannot
silently become several retries plus backoff; the user explicitly requests retry.

The composable additionally owns a 10-second UI deadline. When it expires, it
retires the request generation, aborts the transport, releases loading and presents
explicit retry guidance. This remains useful if an adapter ignores abort or never
settles its promise. A late success/failure cannot clear the timeout or replace a
newer result. Browser suspension/event-loop blocking can delay timers; this is not
a real-time deadline or a guarantee that a server stops computing at 10 seconds.

| Transition | Network/lifetime | Visible state |
| --- | --- | --- |
| Open or explicit refresh | Abort previous read; start a fresh controller/deadline | Loading; discard previous displayed result |
| Successful current result | Clear deadline; release controller | Current totals; clear stale indicator |
| Current failure | Clear deadline; release controller | Explicit error; Refresh available |
| UI deadline | Retire generation before abort | Timeout guidance; Refresh available |
| Close | Retire generation; abort; clear deadline | Panel closed; no late receipt may publish |
| Board/account/token change | Same cancellation | Close and clear all result/error/stale state |
| Board revision change | Same cancellation | Mark stale; require explicit refresh, no new automatic read |
| Unmount | Same cancellation | No later callback owns a mounted view |

Cancellation is a resource-control mechanism, not the correctness proof. The
existing generation guard remains load-bearing because cancellation can arrive
late or be ignored. Only the current generation may publish data, errors or the
`finally` loading transition. Retire generation before calling `abort()` and never
let an obsolete `finally` clear the current request's timer/controller.

## Alternatives and scope

A timeout alone leaves superseded reads alive. Abort alone can leave an unresponsive
adapter's UI loading. A global retry change would affect unrelated operations.
A new generic lifecycle framework is not justified by this bounded seam; reuse
this shape only after another consumer demonstrates the same requirements.

The database snapshot follow-up GH-3058, backend-less demo behavior GH-3056,
rollup arithmetic, zero/unknown/overlap semantics, panel layout and focus behavior
are not changed. No authority, migration, provenance or automation gate changes.

## Tests and verification

Supplied: four API-wrapper cases, 18 composable cases using real Vue with deferred
transport promises/fake clocks, and the existing six component cases with their
call-shape assertion updated to require an AbortSignal. The cases cover every
cancellation boundary, timeout recovery, fresh requests after reopening, late
success and failure, obsolete `finally`, zero automatic retry and timer cleanup.
The stalled-transport tests deliberately do not settle on abort. These are not
physical-device or live-network timing measurements.

Local environment: Linux, Node 22.16.0; the repository requires Node 24.x. The
uploaded ZIP identifies snapshot `9c17a83bfb3a563dd3afa2691cf50439794028ff`.
Touched existing estimate files were hydrated from pinned live main
`44d041ca7d819d450976fc974e14623718f2f467` and their original Git blob hashes
verified. The local worktree is not a complete checkout of that live commit.

`npm ci --ignore-scripts --offline --no-audit --no-fund` could not install dependencies
(`ENOTCACHED`); a separate connectivity probe could not resolve the registry.
Therefore no local executable frontend verdict is claimed. TypeScript syntax-only
parsing is not typechecking or behavioral testing. Governance and relative-link
checks run locally; hosted runtime/test results belong in the PR receipt.

### First hosted run and bounded test correction

On head `7890eaeed3681dfd54556fc2fd6791f087f8b986`, hosted run
`34717874146` passed frontend lint, typecheck and build on both Linux and Windows.
The Linux frontend suite reported **6,884 passed, three skipped, two failed**.
Both failures were the new API rejection assertions: the asynchronous rejection
matcher raised `Method Promise.prototype.then called on incompatible receiver`.
The other new lifecycle cases completed successfully in that run.

The correction changes only those assertions to explicitly capture rejection and
assert both rejection occurrence and exact error-object identity. It preserves
the original plain-object failure stimulus and the one-request assertion; it does
not loosen the behavior contract or change runtime code. The test-runner internals
behind that matcher error have not been independently reproduced locally. A fresh
hosted run must qualify the corrected head; the earlier partial results are not a
full-suite pass and do not qualify this correction.

Required exact-head commands from `frontend/taskdeck-web`:

```sh
npm ci
npx vitest --run --maxWorkers=2 src/tests/api/estimateRollupsApi.spec.ts src/tests/composables/useBoardEstimateRollups.spec.ts src/tests/components/BoardEstimateRollups.spec.ts
npm run lint
npm run typecheck
npm run build
npx vitest --run --maxWorkers=2
```

No canonical STATUS/MASTERPLAN update is made for an unmerged candidate. Existing
OUTSTANDING_TASKS.md physical-phone/keyboard, screen-reader, translation, provider,
release/hosting and CI-control decisions remain open and are not certified here.

## Primary references

- [Axios cancellation](https://axios-http.com/docs/cancellation): request timeout
  and AbortSignal serve complementary purposes.
- [Vue watcher cleanup](https://vuejs.org/guide/essentials/watchers.html#side-effect-cleanup):
  obsolete asynchronous work needs cleanup when its reactive owner changes.
- [Vue lifecycle cleanup](https://vuejs.org/api/composition-api-lifecycle.html#onunmounted):
  manually created timers and connections need explicit teardown.
