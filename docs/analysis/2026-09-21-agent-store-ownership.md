# Agent store request and session ownership

Status: draft PR #3338, 2026-09-21. Base: `307c3b8b50bec1cb0bfaea3e570a942bcb1d4451`.

## Reproduced defect

`agentStore` previously let every profile, run-list and run-detail response write its shared surface. Route-clear helpers changed visible values but did not invalidate requests. The store also had no session lifetime, so old-account work could settle after logout/login, including login as the same user ID.

This admitted several related schedules:

- reverse-settling profile reads were last-response-wins;
- A-old → B → A-new run-list and run-detail navigation allowed the old A result to overwrite the newer A result;
- `clearRuns()` and `clearRunDetail()` could be undone by late success or followed by stale error/toast state;
- an older `finally` could clear a newer request's loading owner;
- pending old-session reads could repopulate a replacement session.

## Contract

Profiles, run lists and run details have independent owner lanes. Each owner carries the current session epoch and a unique request token. A newer request retires only the previous owner in its lane. Route-clear helpers invalidate their own lane before clearing visible state.

The store watches user identity, token, authenticated state and demo state synchronously. Any replacement advances the epoch, retires all owners, and clears every agent surface, error and loading indicator. Stale work still resolves or rejects to its original caller, but cannot patch replacement state, emit a toast or clear a newer loading owner.

Independent lanes remain concurrent. Demo mode retains its no-network behavior. No API, DTO, route, schema, dependency or backend behavior changes.

## Test-first evidence

Test-only head `ae52930c6eb5d5b80a2f6a7347242f4ad60a0036` ran the full frontend suite on Ubuntu and Windows. Both platform jobs passed lint, typecheck, production build and PWA validation, then failed in the new real Pinia suite.

Ubuntu JUnit recorded **7,159 tests, 8 failures, 0 errors**. Eight ownership schedules failed against the original store:

1. reverse-settling profiles installed the old result;
2. A-old → B → A-new runs installed old A;
3. repeated run-detail identity installed old A;
4. `clearRuns()` left the old request's loading flag active;
5. a failure after `clearRunDetail()` installed stale error state;
6. an old `finally` cleared a newer run-list loading owner;
7. session replacement did not clear existing agent surfaces;
8. a stale failure after same-user relogin installed old error state.

The independent-lane concurrency control passed. The same canonical suite failed on Windows, so this is not presented as a platform-only result.

## Verification and remaining gates

The corrected source and committed test transpile under TypeScript 5.8.3 with zero syntax diagnostics. Exact-head hosted lint, typecheck, build and the complete test matrix remain required after the production correction. Independent review should focus on synchronous token/session replacement, route-clear ownership and preserving useful concurrency across the three lanes.

This is client-state integrity, not a claim of server-side authorization bypass or transport cancellation. No merge, release or deployment qualification is claimed by this note.
