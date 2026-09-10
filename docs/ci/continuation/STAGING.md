# Required-workflow staging

Date: 2026-09-10. Owners: #2327 and #2332. Parent: [engineering contract](README.md).

This slice applies the `minimal` transform to `.github/workflows/ci-required.yml`. It changes scheduling dependencies only. It does not select fewer checks on a healthy candidate, remove Windows coverage, alter security enforcement, change permissions, or reuse earlier test results.

## Independent API platform scheduling

The required caller now runs the reusable API suite twice: `api-integration` requests Linux and
`api-integration-windows` requests Windows. Both run the complete existing suite with identical
commands, artifacts and check labels. The reusable workflow defaults to both platforms; unknown
platform inputs also retain both. Linux E2E waits for `api-integration` and every other existing
prerequisite. Windows API qualification remains unconditional and must pass before merge, but it
can overlap Linux E2E. This trades some fail-fast savings when only Windows fails for a shorter
healthy-candidate dependency chain; it does not omit qualification.

The motivation is measured, not a speedup claim: run `34423790319` took 453 seconds for Linux API,
1,200 for Windows API, and 523 for E2E. The latter started only after the aggregate API caller
finished. Two diagnostic original-head runs used 4,840 versus 4,651 runner-seconds and 1,710 versus
1,772 seconds elapsed; they are unmatched observations, so no causal savings estimate follows.
Confirm the split's emitted check names and actual overlap in hosted CI before accepting it.

The transformer recognizes both the original and explicitly split API topology. It refuses a
misrouted or conditional Windows caller. Rollback removes the Windows caller and the Linux
platform input together, restoring the reusable workflow's full two-platform default.

| Work | New prerequisite |
| --- | --- |
| Backend Unit and API Integration | Backend Architecture and Release Workflow Contract |
| Migration Validation | Backend Architecture |
| Frontend Unit | Release Workflow Contract and Paper Color Audit |
| Container Images | Release Workflow Contract |
| E2E Smoke | Frontend Unit, Release Workflow Contract and Paper Color Audit, in addition to every existing dependency |

The short control checks can prevent launching dependent expensive work after a known failure. The API job deliberately does not wait for the entire Backend Unit matrix: the `compute` proposal is optional, not adopted here. Security jobs remain independent. In particular, Secret Scan only runs on PR events and must not become an unconditional dependency on push/merge-group events.

The original workflow was read at immutable base `6c51b09bcdcefbc7a852a7047aa9ab8ee5eb10b7`. Its source blob `f056d76a0c1b4ecabd95783d47131dfe096121c5` was reproduced byte-for-byte before transformation. The transform checks that removing `needs` from each changed job leaves identical bytes. It preserves the complete header/comments, existing check names, reusable callees, commands, action pins, matrices, event/concurrency rules and permissions. The historical header's advisory/enforcing shorthand is deliberately not edited by a dependency-only PR; executable settings remain the authority.

## Qualification

Run the checked-in topology regressions through the existing self-test bridge:

```sh
node --test scripts/ci/smart-ci/continuation.test.mjs
```

Local result for this slice: **257 passed, 0 failed/skipped/cancelled**, Node 22.16.0/Linux. Four regressions exercise the actual checked-in workflow; the existing transformer suite covers unknown shapes, cycles, duplicate dependencies, idempotence and preservation. Local PyYAML parsing is only a syntax sanity check, not GitHub workflow semantic validation.

Configured-Node hosted Smart CI, Actionlint, full required CI and independent/maintainer reviews remain required on this PR head. Do not equate a successful core fixture with product qualification. No cancellation API is called: the transform prevents dependent jobs from starting after failed prerequisites but cannot stop work already running.

## Measurement and rollback

Compare matched change/risk strata and include failed/cancelled attempts, not just green runs. Record total runner seconds and healthy-candidate latency separately; a dependency barrier can reduce failed-run cost while increasing green latency. Include queue/setup time and collector overhead when available; do not fill missing data with zeros.

The retained August baseline is historical and is not an after-measurement. Capture a fresh window using the existing estate measurement tool only after the topology is deployed. No numerical savings are claimed in this PR.

Rollback is the inverse of these `needs` additions. Keep every newer concurrent hardening change. Do not restore an entire old workflow file over subsequent edits. The canonical policy remains shadow and all human settings/review gates remain unchanged.
