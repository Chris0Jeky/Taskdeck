> **Validated 2026-09-02 against `main` `de488fea0`.**
>
> - **The control plane is already on `main`, and further along than the blueprint's "PRs #2341 and #2342 were open" snapshot.** `ci/policy.v1.json`, `ci/README.md`, `ci/schemas/{ci-policy,ci-plan,ci-run}.v1.schema.json`, `scripts/ci/smart-ci/{plan,evaluate-gate,measure-ci-estate,action-pins,artifact-cleanup}.mjs` with `lib/{plan,glob,estate}.mjs`, and `.github/workflows/{smart-ci-shadow,smart-ci-self-test}.yml` all exist. The mode is `shadow`: nothing changes job selection yet.
> - **`ci-run.v1` is not a proposal — it is a shipped file, and the bundle's version conflicts with it.** `ci/schemas/ci-run.v1.schema.json` on `main` is camelCase with `$id` `https://taskdeck.dev/...` and a required set beginning `schemaVersion, kind, mode, ok, wouldFail`; the bundle's same-named, same-versioned file is snake_case with `$id` `https://taskdeck.local/...`. Both set `additionalProperties: false`, so no document satisfies both. The blueprint's "Receipt model" list is a good **field inventory** to add to the shipped schema — whose own description already names CI-12 `#2336` as the extender — and must never be copied over it.
> - **The API shard names in this blueprint are fictional.** No `Taskdeck.Api.IntegrationTests` assembly exists; the suite is `backend/tests/Taskdeck.Api.Tests` with **176** test classes, and of the ten classes the bundle names only `MigrationBootstrapTests` exists. The seven *behavioural* shard names are still a reasonable taxonomy; the class assignments are not.
> - **The measured platform delta is 7.2 vs 17.3 minutes (mean, 30 green runs), a 2.4x ratio and 35.7 allowance minutes** — `docs/ci/CI_BASELINE.md`, which supersedes the PR #2280 single-run sample the blueprint's inputs came from.
> - **"Fix process overhead before adding parallel jobs" is now an ADR obligation, not advice.** ADR-0066 §Decision 6 requires the Windows MCP/process harness to be *repaired* rather than merely sharded, and private-repository accounting rounds every job up to a whole minute and doubles Windows — so a premature shard split raises the bill.
> - **Nightly consolidation cannot come first.** `docs/ci/SMART_CI.md` §9 places it in phase 3, gated on 100% recall and an existing weekly sweep; the blueprint's CI10 1→2→3→4 order inverts that.
> - **Flake governance is already accepted policy**, not a proposal: ADR-0066 invariant 9 and §Alternatives Considered ("Retry-to-green … Rejected"). What is missing is enforcement — `ci/quarantine.v1.json` and its checker do not exist.
> - **Budget posture is unchanged and correct**, but "missing receipt fails the gate" must wait until `ci/policy.v1.json` leaves `mode: shadow`, or it manufactures false reds inside CI-03's 20-PR observation window.
>
> The body below is the bundle text, unedited.

# Smart CI depth blueprint

## Constraint

Milestone-5 CI work must extend the canonical Smart CI baseline/policy/receipt/control plane from milestone 4. At the snapshot, PRs #2341 and #2342 were open. Do not create parallel planners, policy digests or receipt roots.

## Control loop

```text
change + trust + historical evidence
  → risk classifier
  → selected lanes / skipped-with-reason
  → runner broker
  → test/gate execution
  → ci-run receipt
  → budget/flake/duplicate analysis
  → weekly evidence + owned findings
  → policy adjustment through reviewed config/ADR
```

## API shards

Initial explicit classes:

- api-core;
- auth-security;
- capture-proposal-executor;
- mcp-contract;
- mcp-process-http;
- journeys-realtime;
- persistence-migration.

A union gate enumerates the test assembly and fails on missing or multiply assigned classes. Traits may replace the manifest only after they provide the same exactness.

Fix process overhead before adding parallel jobs:

- build once;
- readiness probes instead of sleeps;
- central port reservation;
- deterministic cancel/grace/kill;
- safe shared host fixtures;
- failure-only process logs;
- TRX timing history.

## Receipt model

`ci-run.v1` should include:

- repository/run/workflow/attempt and exact SHAs;
- policy/version/digest, risk and trust inputs;
- selected and skipped lanes with reasons;
- per-job runner/hosted flag, queue/setup/test/total seconds, result, tests, rerun state, cache and artifact bytes;
- critical path and aggregate runner seconds;
- hosted minutes/cost with rate-table version;
- self-hosted wall time;
- flake and duplicate-exact-SHA markers;
- links/IDs, never source or test-log content.

## Nightly/release

- Find the last successfully deep-qualified main SHA.
- No relevant changes produce a green “no new evidence required” receipt.
- Relevant changes select affected deep suites.
- Once weekly run the complete OS/browser/container/security/perf entropy sweep.
- Release checks out the exact tag in a clean hosted context, rebuilds, verifies migration/upgrade, SBOM, provenance and digests, then signs in the protected environment.
- Collapse duplicate tag/release triggers for the same SHA.

## Flake governance

- One diagnostic rerun maximum.
- Attempt one remains visible.
- Rerun-green = flaky, never clean.
- Quarantine requires exact test/pattern, issue, owner, reason, created/expiry and compensating coverage.
- Expired entry fails governance.
- Recommended default 14 days, hard maximum 30 without maintainer exception.

## Budget posture

Start warning-only while measurements stabilize. Every warning names lane, observed P95/critical path, budget, delta and likely reason. Promote to blocking only after sample count and variance are adequate.

Provisional targets from the issue:

- R0/R1 ≤5 minutes;
- R2 ≤10;
- R3 ≤20;
- main verifier ≤5.

Optimize defects caught/risk retired per critical-path minute and compute—not skipped job count.
