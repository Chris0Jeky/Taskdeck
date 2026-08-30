# Smart CI Fabric — architecture and operating model

Last Updated: 2026-08-30 · Decision: [ADR-0066](../decisions/ADR-0066-smart-ci-fabric-and-private-repository-runner-trust.md) · Tracker: CI-00 `#2324` · Mode: **personal GitHub Pro account, shadow phase**

Taskdeck's CI is treated as a **verification policy engine**, not a static job list. For every
proposed repository state it answers: what changed, who produced it and how much the execution
environment may trust it, which failures the change could plausibly cause, which checks retire that
risk for the least critical-path time and compute, where they may run safely, and what evidence
proves the commit is admissible.

```text
event
  → immutable change inventory (base/head/merge SHA, changed paths, actor association, labels)
  → trust class T0–T4
  → risk class R0–R4 and impact plan (selected lanes + reasons, skipped lanes + reasons)
  → execution: GitHub-hosted clean control · isolated self-hosted heavy (trusted only)
  → evidence aggregation (content-free receipt bound to the exact SHAs and policy digest)
  → one stable required check: `Smart CI / Required Gate`
  → merge (branch current) → landed verifier on main → change-driven deep qualification → release
```

## 1. Invariants

1. **No required check disappears because a filter skipped a workflow.** The planner and the gate always report.
2. **Unknown change = full escalation.** Unmapped path, planner error, invalid policy, changed control path, missing base comparison → the full conservative plan.
3. **Self-hosted never means trusted by default.** Trust derives from event, actor association, changed control files and policy — not from a runner label or an `if:`.
4. **A self-hosted runner holds no secret repository code could extract.** Signing/release credentials exist only in the protected release context (`#2149`).
5. **The plan is evidence.** Policy digest, changed paths, reasons, selected/skipped lanes, runner class and budget forecast are persisted per run.
6. **The gate is stable.** Branch protection requires one named context, not a moving set of reusable-workflow child names.
7. **Release evidence is clean-room evidence.** Artifacts are rebuilt from the tag; PR artifacts are never promoted.
8. **Platform coverage follows platform risk.** Linux is the semantic baseline; Windows and macOS verify their distinct contracts.
9. **Flakes are defects.** A rerun may classify, never silently turn a failed required run green.
10. **Policy changes are code changes with a higher trust class.** Workflow, planner, policy, bootstrap, and release changes run hosted-only (R4/T2).

## 2. Risk classes

| Class | Typical change | Required evidence (ordinary PR) |
| --- | --- | --- |
| R0 | ordinary Markdown, inert assets, issue templates | docs governance, diff secret scan, planner/gate |
| R1 | one component/helper and its tests; a local script without release/security effect | affected unit tests, lint/type/build for the affected ecosystem, secret scan, planner/gate |
| R2 | application service, API endpoint, store/repository, frontend flow, capture/proposal behaviour without migration/security boundary change | full affected Linux semantic suites, API or frontend integration slice, selected E2E journey when the user-visible contract changes, selected Windows contract when platform-sensitive files change |
| R3 | persistence/migrations, auth, executor/proposal application, MCP process/transport, SignalR, desktop runtime, containers/deploy, worker protocol, shared DTO/serialization | full Linux semantic qualification, migrations, targeted Windows compatibility, integration + golden E2E, clean static/security checks, container integration where relevant |
| R4 | `.github/**`, `ci/**`, `scripts/ci/**`, package bootstrap, release/signing/provenance, security allowlists, governance scripts | GitHub-hosted only: actionlint/workflow contracts, planner + policy self-tests, SHA-pin guard, full required qualification where runtime selection could change |

## 3. Trust classes

| Class | Input | Execution eligibility |
| --- | --- | --- |
| T0 | control-plane code from the protected base | hosted control jobs |
| T1 | same-repository PR by the maintainer or an approved agent, no control-path change | isolated self-hosted heavy jobs allowed, no secrets |
| T2 | same-repository PR changing workflow, planner, policy, bootstrap, release or security controls | hosted only until merged and re-qualified |
| T3 | fork, Dependabot-like branch, collaborator without elevated trust, ambiguous actor | hosted, read-only, no secrets, never a persistent self-hosted runner |
| T4 | protected tag/release | dedicated release context; credentials only in protected steps |

Risk and trust are independent: a one-line workflow change is R4/T2.

## 4. Control-plane placement in personal-account mode

A pull request can edit `.github/workflows/**`. On a personal account there is no organization
ruleset-required workflow and no workflow-restricted runner group, so the control plane is placed
where the PR cannot edit it:

- The planner and the gate run on **`pull_request_target`**, whose workflow file and checked-out
  tooling come from the PR's **base** commit. They declare `permissions: contents: read,
  pull-requests: read`, read the changed-file list from the API, and **never check out or execute
  head code**. Head-side code runs only on `pull_request` jobs (the existing required lane, and the
  planner's own self-tests as the R4 hosted-control lane).
- The base policy is the ceiling: a PR may request more (`ci:full`, `ci:hosted`), never less.
- **Residual, recorded:** a same-repository PR can still author a `pull_request` job named like the
  gate, or rewrite the `pull_request` workflows. Mitigations: every such change is R4 and receives a
  fresh-context adversarial review under the review-and-ship pipeline; the repository has no external
  contributors while private; the organization upgrade path is CI-14 `#2338` with its triggers.
- Self-hosted runners are **never attached while the repository is public**.

## 5. Lanes and where they run

The lanes are the existing reusable workflows (ADR-0013), parameterized rather than duplicated.

| Lane | Reusable workflow / job | Default runner class | Notes |
| --- | --- | --- | --- |
| control | `smart-ci-shadow.yml` planner + gate, docs governance, release-workflow contract, paper colour audit, planner self-tests | hosted Linux | consolidate sub-minute jobs sharing trust/permissions (CI-03) |
| secret-diff | `reusable-gitleaks.yml` (pr mode) | hosted Linux | every PR |
| dependency-signals / sast | `reusable-dependency-security-signals.yml`, `reusable-sast-scanning.yml` | hosted Linux | required contexts (ADR-0035) |
| architecture | `reusable-backend-architecture.yml` | hosted Linux → self-hosted Linux | |
| backend-unit | `reusable-backend-unit.yml` (Domain/Application/CLI) | Linux semantic; Windows only via the contract | |
| api-integration | `reusable-api-integration.yml` → behavioural shards (CI-06) | Linux semantic; Windows contract shard | |
| migration | `reusable-migration-validation.yml` | hosted Linux | selected by persistence groups |
| frontend | `reusable-frontend-unit.yml` | Linux once; Windows launcher/platform subset | CI-08 |
| e2e-journey | `reusable-e2e-smoke.yml` by journey | self-hosted Linux / hosted | CI-08 |
| containers | `reusable-container-images.yml`, `reusable-container-integration.yml` | self-hosted Linux / hosted | risk-gated (CI-08) |
| windows-compat | worktree-helper harness, launchers, dev-up, MCP host, SQLite, desktop | Windows (self-hosted after CI-04) | CI-07 |
| deep / weekly | backend solution, cross-browser, k6, coverage, full Windows | self-hosted / hosted | CI-10 |
| release | `ci-release.yml`, `release-security.yml`, `release-desktop.yml`, `release-container.yml` | hosted clean-room; signing in the protected context | CI-10 |

Execution mode: repository variable `CI_EXECUTION_MODE` = `hosted` (default until CI-04) |
`hybrid` | `self-hosted`; the `ci:hosted` label forces hosted for one PR. T2/T3/R4 plans never
resolve to a self-hosted class regardless of mode. There is no transparent fallback: an offline
self-hosted runner leaves its job queued and the gate pending.

## 6. Event topology

- **Draft PR:** planner + R0/R1 feedback (docs governance, secret diff, impacted fast tests); R3/R4 changes still qualify early. No heavy estate after every micro-commit.
- **Ready PR / `ci:qualify`:** the full selected plan against the merge ref; superseded runs cancel (`concurrency` per PR, already in place).
- **Push to `main`:** the landed verifier — receipt lookup by **tree SHA** (branch-current makes the PR's synthetic merge tree equal to the landed merge commit's tree), docs governance, architecture invariant, one startup smoke, ≤5 minutes. No receipt (direct push, bypass) → full hosted qualification.
- **Nightly:** one coordinator; no relevant `main` change since the last deep receipt → honest green "no new evidence required"; otherwise the affected deep suites; weekly full sweep (Linux, Windows, browsers, containers, dependency/SAST, performance). Mutation stays manual (ADR-0052).
- **Release:** from the exact tag on a clean hosted runner, no untrusted build-output cache, rebuild, migrations/upgrade check, SBOM + provenance, digest verification, signing only through the protected environment, publish the exact qualified artifacts.

## 7. The gate

`Smart CI / Required Gate` always runs (`if: always()`), reads the plan receipt and every selected
job's conclusion, and fails when: planning failed or escalated work did not run; a selected job
failed, was cancelled, timed out or is missing; a job was skipped without a policy reason; a receipt
references another SHA or policy digest; required hosted control evidence is absent; or a hard
safety budget was exceeded without an authorized override. In the **shadow phase** the gate is
red only for planner/schema defects and otherwise reports the would-be verdict; it is registered in
branch protection (with `strict: true`) only after ≥20 PRs without a false red (CI-03).

## 8. Receipts and reports

Every gate run writes `ci-run.json` (schema `ci/schemas/ci-run.v1.schema.json`): SHAs and merge
tree, policy digest, risk/trust, selected/skipped with reasons, per-job runner class / hosted flag /
queue / setup / test / total seconds / allowance-minute estimate / tests run-failed-skipped / rerun /
cache hit / artifact bytes; summary critical path, aggregate runner seconds, hosted minutes and cost
estimate, self-hosted wall seconds, flake and duplicate-qualification flags. Names, ids, timestamps
and sizes only — never log or user content. The weekly report (CI-12) turns receipts into
P50/P95 critical path, minutes per merged PR, hosted cost, queue delay, selection and failure yield
per lane, flake rate, slow-test regressions, duplicate exact-SHA runs, cache utility and storage.
Provisional budgets: R0/R1 ≤5 min, R2 ≤10, R3 ≤20, main verifier ≤5 — a regression names the lane.

## 9. Rollout (evidence-gated)

| Phase | What lands | Gate to the next phase |
| --- | --- | --- |
| 0 measure | `measure-ci-estate.mjs`, `docs/ci/CI_BASELINE.md` | baseline committed |
| 1 shadow | policy + planner + schemas + fixtures; `smart-ci-shadow.yml`; observation-mode gate; action-pin inventory | ≥20 PRs; recall report; zero false reds |
| 2 gate | gate evaluator with receipts; landed verifier; consolidated control job; **maintainer** registers the gate + `strict` | one merge proven; direct-push escalation proven |
| 3 dedupe | `push: main` full run removed; Windows frontend full run removed; Windows API → contract shard; containers risk-gated; nightly consolidated | recall stays 100%; weekly sweep exists |
| 4 runners | isolated VMs bootstrapped; broker; runbook; rehearsals (hosted-only while public) | after cutover: **maintainer** registers runners, mode → `hybrid` |
| 5 cutover | CI-13 checklist executed; **maintainer** flips visibility | private-mode R0/R2/R4/merge/nightly/release runs green |
| 6 depth | API shards + harness repair, receipts/weekly report, flake quarantine, org decision (CI-14) | as measured |

## 10. Commands

| Purpose | Command |
| --- | --- |
| Measure the estate (read-only) | `node scripts/ci/smart-ci/measure-ci-estate.mjs --since YYYY-MM-DD --until YYYY-MM-DD --sample 30 --out-dir docs/ci/baselines` |
| Measurement helper tests | `node --test scripts/ci/smart-ci/measure-ci-estate.test.mjs` |
| Planner / gate self-tests (CI-02 scaffold) | `node --test scripts/ci/smart-ci/*.test.mjs` |
| Plan one change locally (what-if, no event payload) | `node scripts/ci/smart-ci/plan.mjs --policy ci/policy.v1.json --base-sha <sha> --head-sha <sha> --changed-files <one path per line> --out ci-plan.json` |
| Evaluate a plan receipt locally | `node scripts/ci/smart-ci/evaluate-gate.mjs --plan ci-plan.json --policy ci/policy.v1.json --mode shadow` |
| Action pin inventory | `node scripts/ci/smart-ci/action-pins.mjs` |
| Runner VM broker (Hyper-V, no GitHub calls) | `scripts/ci/runners/Invoke-TaskdeckCiRunnerVm.ps1 -Action Status` |

## 11. File map

```text
ci/policy.v1.json                          versioned policy (risk/trust/runner classes, control paths, path groups, lanes)
ci/test-ownership.v1.json                  production boundary → lanes (CI-05)
ci/schemas/                                policy, plan, receipt schemas
scripts/ci/smart-ci/plan.mjs               deterministic planner
scripts/ci/smart-ci/evaluate-gate.mjs      gate evaluator
scripts/ci/smart-ci/measure-ci-estate.mjs  estate measurement (CI-01)
scripts/ci/smart-ci/action-pins.mjs        external action pin inventory (CI-11)
scripts/ci/runners/                        VM bootstrap, broker, runbook (CI-04)
.github/workflows/smart-ci-shadow.yml      shadow planner + observation gate
docs/ci/                                   this document, threat model, cutover checklist, baselines
docs/analysis/2026-08-30-smart-ci/         the pack as received + reconciliation
```
