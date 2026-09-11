# Source-launcher and frontend verification boundaries

Date: 2026-09-10. Owners: #2331, #2332 and #2329. Parent: [engineering contract](README.md).

`reusable-frontend-unit.yml` defines two independent job families. `source-launcher` runs the existing regression once on hosted Linux; `frontend-unit` retains the complete Linux/Windows matrix and every previous frontend semantic check. No launcher source or test implementation is changed.

The exact launcher command remains `node --test --test-concurrency=1 --test-timeout=30000 scripts/ci/dev-up.test.mjs`. Its Linux guard and ten-minute step timeout are unchanged. The separate job has a fifteen-minute ceiling for checkout/Node setup around the suite's own watchdog. Node uses the caller's version input. The new checkout disables credential persistence; preserve concurrent checkout hardening during integration. PowerShell cases remain governed by SC-3; this slice does not change #2858's cleanup implementation.

## Independent results without lost qualification

Previously a launcher failure stopped the Linux frontend job before lint/typecheck/build/coverage, and backend changes affected that mixed job's input identity. Sibling jobs can finish independently. A launcher failure still fails the reusable call/E2E prerequisite; it does not erase an independently successful frontend result. The caller's `needs: frontend-unit` waits for both families.

Parallelism introduces one extra job's setup/rounding overhead. No measured savings are claimed. Full frontend coverage stays full; no partial threshold is substituted.

## Canonical and historical accounting

The existing shadow policy gains `source-launcher-linux`, context `Frontend Unit / Source Launcher (Linux)`, using hosted Linux. Its ownership is added to fifteen existing backend/frontend/launcher/script groups, without changing ANY existing group ID, path pattern or risk floor. Unknown paths therefore retain their original escalation and nightly suite mapping remains valid. No required check is registered.

The adapter gives the new lane the backend/frontend/script closure. Post-split frontend fingerprints no longer depend on backend sources. Historical policies without the new lane retain the original broad compound-job contract; old evidence is not reinterpreted retroactively. All Taskdeck contracts remain unreviewed/reuse-disabled.

Original policy blob: `8fc840fac37bef19790e3b4ec6e03280ea5c81ee`. Removing the new lane and its existing-group references reconstructs the original parsed policy exactly. Canonical receipt schema versions are unchanged.

## Hosted integration correction

The first hosted head `e66711b3` exposed nine self-test failures: a broad new ownership group lacked a nightly mapping and changed an overlap fixture; an inherited E2E test forbade the newly intended frontend barrier. The correction removes the broad group rather than weakening the nightly validator, inherits only existing ownership, and incorporates the parent E2E contract correction. Earlier failures remain recorded. Fresh hosted qualification is required.

Local continuation plus launcher-placement tests originally passed 264/264 on Node 22.16.0/Linux, but that was not the complete existing Smart CI suite. A later source-overlay report recorded 309 tests including in-progress provider tests. The current cumulative Windows control run and hosted scheduling sample are recorded in [operations](OPERATIONS.md#verification-and-retained-failures). Earlier fixture success does not qualify the failed hosted head, and each changed base still requires its own full hosted qualification.

## Rollback

Restore the original launcher step when removing the separate job; never remove the new job alone. Remove its lane and existing-group references, and restore the broad adapter contract together. Preserve concurrent launcher fixes/credential hardening. Keep the canonical testing guide and Smart CI topology description synchronized with the resulting job boundaries.
