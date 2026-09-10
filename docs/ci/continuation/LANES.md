# Source-launcher and frontend verification boundaries

Date: 2026-09-10. Owners: #2331, #2332 and #2329. Parent: [engineering contract](README.md).

`reusable-frontend-unit.yml` now defines two independent job families. `source-launcher` runs the existing launcher regression once on hosted Linux; `frontend-unit` retains the complete Linux/Windows matrix and every previous frontend semantic check. No source launcher or test implementation is changed.

The launcher command remains:

```sh
node --test --test-concurrency=1 --test-timeout=30000 scripts/ci/dev-up.test.mjs
```

Its Linux guard and ten-minute step timeout are unchanged. The separate job has a fifteen-minute ceiling to allow checkout/Node setup around that step and retain the suite's own watchdog. Node uses the caller's existing pinned version input. The new checkout disables credential persistence; existing checkout hardening from concurrent PRs must be preserved when integrating. PowerShell cases remain governed by SC-3; this slice neither reintroduces them into hosted Windows nor changes #2858's cleanup implementation.

## Why a distinct job

Previously a launcher failure stopped the Linux frontend job before lint/typecheck/build/coverage, and backend changes affected that mixed job's input identity. The new sibling jobs can finish independently. A launcher failure remains visible and fails the reusable workflow; it does not erase an independently successful frontend result. The caller's `needs: frontend-unit` still waits for the reusable call, including both families, before E2E.

Parallelism can improve healthy feedback latency but introduces one extra hosted job's setup/rounding overhead. No measured savings are claimed. The important first result is an honest task boundary for future evidence reuse. Full frontend coverage stays full; no partial coverage threshold is substituted.

## Canonical policy and historical compatibility

The existing shadow policy gains exactly one lane, `source-launcher-linux`, whose context is `Frontend Unit / Source Launcher (Linux)`. It uses hosted Linux without a new self-hosted entitlement. One additive path group covers `backend/**`, `frontend/**` and `scripts/**` conservatively. All old lanes/rules/security settings remain; policy mode is still `shadow`. No required check is registered.

The continuation adapter recognises the new lane and gives it the backend/frontend/script launcher closure. With a post-split policy, frontend semantic fingerprints no longer depend on backend sources. When inspecting a historical policy without the new lane, the adapter retains the old broad mixed-job contract; it cannot reinterpret old frontend evidence as if the split had always existed. All Taskdeck contracts remain unreviewed and reuse-disabled.

The original policy blob was `8fc840fac37bef19790e3b4ec6e03280ea5c81ee`. Removing the newly added lane and path group reconstructs the original parsed policy exactly. The canonical `ci-plan.v1`/`ci-run.v1` schema versions are not replaced.

## Tests and adoption

The updated root `launcher-suite-placement.test.mjs` checks Linux-only exact command/timeout, a single invocation, semantic independence, retained OS matrix, unconditional lint/type/build/coverage, read-only credentials and the new canonical lane. The continuation bridge also exercises current input closure and keeps its legacy-policy regression.

Local combined continuation + placement run: **264 passed, zero failed/skipped/cancelled**, Node 22.16.0/Linux. This does not execute the product launcher suite, full frontend suite or the complete pre-existing Smart CI suite locally. Those require exact-head hosted qualification, plus independent and maintainer review under SC-10. A coordinator should reconcile the canonical Smart CI topology prose with this scoped implementation document after the stack lands; no leased canonical document is overwritten here.

Rollback must restore the original launcher step and remove its separate job together, then remove the lane/path-group and restore adapter legacy behaviour. Never delete the new job alone and thereby lose the launcher suite. Do not revert concurrent launcher fixes or checkout hardening. Restoring any original compound-job proof still requires that compound input contract; isolated frontend proofs cannot qualify it.
