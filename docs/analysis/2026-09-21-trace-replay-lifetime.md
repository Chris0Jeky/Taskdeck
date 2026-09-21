# Trace replay execution lifetime

Status: unpublished draft candidate, 2026-09-21. Base: `307c3b8b50bec1cb0bfaea3e570a942bcb1d4451`.

## Reproduced defects

Repeated `play()` calls could schedule duplicate delivery. A handler awaiting a
promise retained permission to advance state, emit an error or schedule work
after `stop`, `seekTo` or `dispose`. Pause/resume during that promise could invoke
the same action again. Reentrant state listeners could receive an obsolete state
after a preceding listener had stopped playback. Non-finite speed and fractional
or non-finite seek indices were accepted.

## Contract and structure

One timer and one active execution own the current generation. Stop, seek and
dispose revoke that ownership; every awaited handler checks it before committing
state or proceeding to another handler. Timers capture an immutable index and
generation. Timer cleanup and scheduling have one implementation each.

Pause allows the already-running action to settle once, but schedules no
successor. Resume does not invoke that pending action again. Disposal is final;
subsequent controls and handler registration do nothing. A completed/error trace
can be sought and resumed at the selected index. State-emission revisions stop
obsolete notifications after synchronous observer reentry.

This engine cannot undo or cancel external effects already started by a handler.
It suppresses obsolete continuations; it does not claim transactional cancellation.
The engine is internal tooling, currently used by `DevToolsView.vue` for analysis.
No routes, application data, dependency manifests, schemas or CI controls change.

## Verification and remaining gates

Supplemental direct-production execution on Node 22 passes 22 cases; the same
final suite exposes 17 failures on the original code. A self-review added the
reentrant-observer regression, observed it fail, then corrected it.
A standalone TypeScript 5.8.3 production-module check passes.

A new `traceReplayLifecycle.spec.ts` contains canonical Vitest regressions. It is
not added to the existing spec quarantine. The established `traceReplay.spec.ts`
is retained unchanged. Neither Vitest suite has been run here: the required Node
24 runtime/dependencies were unavailable and npm registry DNS failed.

Before ready-for-review: run both replay suites and the full frontend lint,
typecheck, build and Vitest commands on the pinned toolchain; require exact-head
hosted CI and independent review. Particular review targets are pending-handler
pause/resume semantics, reentrant callbacks and final disposal. No merge, release
qualification or independent-review approval is claimed.
