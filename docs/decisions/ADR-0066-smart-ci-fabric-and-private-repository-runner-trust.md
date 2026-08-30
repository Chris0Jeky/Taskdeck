# ADR-0066: Smart CI Fabric and Private-Repository Runner Trust

- **Status:** Accepted under delegation (2026-08-30) — the maintainer directed the programme in-session ("the repo will need to go private for v0.3.0 main release, and initially it should work with only my github pro account, without having to rely on teams/enterprise"); the agent pass ruled on the pack's open conditions and recorded every ruling on tracker CI-00 `#2324`, where one reply overturns any of them. The four actions only the maintainer can perform — repository visibility, spend ceiling, branch-protection/ruleset edits, runner registration — are **not** delegated and stay open in `OUTSTANDING_TASKS.md` §J and on CI-13 `#2337`.
- **Date:** 2026-08-30
- **Deciders:** Maintainer (direction), agent pass under delegation (rulings)
- **Related:** ADR-0013 (CI topology — retained), ADR-0035 (required security-scan gate — amended), ADR-0052 (CI estate right-sizing — retained, branch-protection paragraph superseded), ADR-0051 (autonomous admission), ADR-0061 (trusted shared instance), `#1173`, `#1275`, `#2237`; tracker CI-00 `#2324`, children `#2325`–`#2339`; evidence `docs/analysis/2026-08-30-smart-ci/`.

## Context

Taskdeck's CI is rigorous and well decomposed (ADR-0013: reusable workflows composed by `ci-required.yml`, `ci-extended.yml`, `ci-nightly.yml`, `nightly-quality.yml`, `ci-release.yml`, `release-security.yml`), but its economics assume a public repository with unlimited hosted minutes. Measured on 2026-08-30 (`docs/ci/CI_BASELINE.md`):

- the required workflow runs the **whole** suite on `pull_request`, again on every `push` to `main` (a merge commit), and on `merge_group`; a green PR run has 17 jobs and a 24.7-minute p50 critical path (p95 37.9);
- Windows runs as a second semantic platform (full backend unit, full API integration, full frontend lint/type/build/coverage after a launcher regression, plus the worktree-helper harness) — the API suite alone averages 17.3 minutes on Windows against 7.2 on Ubuntu, and the delta is process startup, readiness polling, fixed delays and teardown in the MCP HTTP/process suites, not semantics;
- the Actions cache sits at its 10 GiB cap (183 caches, evicting) and **32,051 artifacts** are on record, 18,334 of them unexpired = **372.1 GB** against a 1 GB private allowance;
- `main` branch protection requires only the three security-scan contexts, `strict: false`, `enforce_admins: false`; `ci-required` itself gates by convention (ADR-0052 recorded this; nothing changed it);
- only one external action is SHA-pinned; `sha_pinning_required` is off; no self-hosted runner exists.

On a **personal GitHub Pro** account a private repository includes 3,000 Actions minutes/month (Windows minutes count x2, macOS x10, every job rounded up to a whole minute) and 1 GB of Actions/Packages storage billed daily beyond that. Merge queue is unavailable to a personal account (it is an organization-plan feature); organization rulesets, organization-required workflows and workflow-restricted runner groups are unavailable (Team/Enterprise only). The maintainer's 2026-08-30 Smart CI pack (archived as received under `docs/analysis/2026-08-30-smart-ci/`) proposed a policy-driven **Smart CI Fabric** with an organization-owned control plane as the preferred boundary; the directive is to make it work on the personal account first.

Two constraints shape everything below. A self-hosted runner executes repository code — agent-authored code included — on the maintainer's hardware, and GitHub's own guidance is that such a runner is neither clean nor guaranteed ephemeral. And a pull request can edit `.github/workflows/**`, so in a personal repository nothing GitHub-native stops a PR from redefining the jobs that produce its own checks; the defence is placement (base-ref control plane), review (every workflow change is R4 and reviewed), and honesty about the residual.

## Decision

Adopt the **Smart CI Fabric** in **personal-account mode**, staged behind evidence:

```text
change inventory → trust class (T0–T4) → risk class (R0–R4) → selected verification graph
   → hosted / isolated self-hosted execution → evidence aggregation → one stable required gate
```

1. **GitHub Actions remains the control plane.** ADR-0013's reusable-workflow decomposition is retained and parameterized (runner class, selected shard); no CI-provider migration is authorized to obtain free minutes.
2. **One stable required check: `Smart CI / Required Gate`.** It always runs, evaluates the plan receipt and every selected job's conclusion at the exact base/head/merge SHA and policy digest, and fails on missing, cancelled, timed-out, wrong-SHA, or skipped-without-policy-reason evidence. It is registered in branch protection only after observation-mode evidence (CI-03 `#2327`); `strict` (branch must be current) is switched on with it; ADR-0035's three security contexts stay required through the migration. Branch-current replaces merge queue.
3. **Pull requests carry the substantive qualification; `main` gets a landed verifier.** A normal merge runs a bounded verifier (≤5 min) that matches the landed commit's **tree SHA** to a PR receipt qualified while the branch was current, then runs docs governance, the architecture invariant and one startup smoke; a direct or bypass push (no receipt) escalates to full hosted qualification. `merge_group` is not a design dependency.
4. **Risk and trust are independent.** Risk R0 (docs/inert) → R4 (CI, release, supply-chain, security control); trust T0 (protected control-plane code) · T1 (same-repo product change, maintainer or approved agent, no control-path change) · T2 (same-repo control change) · T3 (fork, Dependabot, ambiguous actor) · T4 (protected release). T2/T3/R4 always execute hosted.
5. **Deterministic, fail-closed selection in shadow mode first.** A versioned policy (`ci/policy.v1.json`) and planner (`scripts/ci/smart-ci/plan.mjs`) classify each change; unknown path, planner error, invalid policy, changed control path, or missing base comparison select the full conservative plan. Selection changes no job until a recall report over ≥20 PRs proves every failure the full suite caught would have been selected (CI-02 `#2326`, CI-05 `#2329`); lane families switch from shadow to selecting one at a time, each behind its own evidence (CI-07 `#2331`, CI-08 `#2332`).
6. **Linux is the semantic baseline; Windows is a compatibility contract.** Domain, application, ordinary API, frontend, architecture and primary E2E semantics run once on Linux. Ordinary-PR Windows coverage is a bounded contract (launchers, PowerShell, paths, process/stdio, SQLite locking, desktop startup, MCP host, worker containment, packaging); the full Windows matrix stays weekly and at release qualification until parity evidence justifies less. API integration is split into behavioural shards and the Windows MCP/process harness is repaired rather than merely sharded (CI-06 `#2330`).
7. **Hybrid execution with explicit mode.** Hosted: planner, gate, secret/dependency/SAST scans, workflow and control checks, T2/T3 changes, release clean-room, macOS, emergency fallback. Isolated self-hosted Linux VM (desktop): heavy trusted semantic, API, E2E, containers. Isolated self-hosted Windows VM/laptop: the compatibility contract and scheduled full sweeps. `CI_EXECUTION_MODE = hosted | hybrid | self-hosted` (repository variable, default `hosted`) plus a `ci:hosted` label override; there is **no** claimed transparent fallback — an offline runner leaves the gate pending, never green.
8. **Personal-account control-plane placement.** The planner and gate run from the PR's **base** commit on `pull_request_target` with `permissions: contents: read, pull-requests: read`, read the changed-file list from the API, and never check out or execute head code; head-side planner self-tests run on `pull_request`. A PR may request more verification than the base policy (`ci:full`), never less. Residual, recorded not hidden: a same-repository PR can still author a job named like the gate or rewrite the `pull_request` workflows; mitigations are R4 classification, the fresh-context review every workflow change receives, and the CI-14 `#2338` organization upgrade path (ruleset-required workflow + workflow-restricted runner group) whose triggers are recorded there. Self-hosted runners are **never attached while the repository is public**.
9. **Self-hosted runners hold nothing worth stealing.** Isolated VMs (no host mounts, clipboard, SSH agent, browser profile, cloud tokens; NAT outbound only; unprivileged runner account; one job per host; bounded caches; monthly reset from a golden image); ordinary self-hosted jobs get `contents: read` only, `persist-credentials: false`, and no repository/environment secrets; signing and release credentials live only in the protected release context `#2149` defines. Persistent VMs first; one-job ephemeral snapshots when external contributors, secrets in ordinary CI, or an anomaly arrive (`docs/ci/RUNNER_TOPOLOGY_AND_THREAT_MODEL.md`).
10. **Change-driven deep qualification.** One nightly coordinator runs deep suites only when `main` changed since the last successful deep receipt, with a weekly full entropy sweep (Linux, Windows, browsers, containers, dependency/SAST, performance); `CI Nightly` and `Nightly Quality Signals` merge under it; mutation stays manual (ADR-0052 verdict retained). Release qualification rebuilds from the exact tag in a clean hosted context and never promotes PR artifacts (CI-10 `#2334`).
11. **Storage and economics are measured, first.** Every gate run emits a content-free `ci-run.json` receipt; a weekly report tracks critical path, runner-minutes, hosted minutes/cost, queue delay, selection and failure yield, flake rate, slow tests, duplicate exact-SHA runs, cache utility and storage (CI-12 `#2336`). Storage (caches, artifact retention classes) is the first saving to land because it bills daily the moment the repository is private (CI-09 `#2333`).
12. **Supply-chain hardening.** Every external action pinned to a full commit SHA with a version comment, `sha_pinning_required` flipped on afterwards by the maintainer; default token read-only (already true); no `pull_request_target` job checks out or executes untrusted head code; CI-control changes qualify hosted-only (CI-11 `#2335`).

### Relationship to earlier CI decisions

| ADR | Disposition |
| --- | --- |
| ADR-0013 | **Retained.** Reusable-workflow decomposition stays; this ADR adds planner inputs, runner classes, shards and the aggregate gate on top of it. |
| ADR-0035 | **Amended.** The three scans stay in the required lane and stay required contexts; the "one aggregation gate job — rejected for this change" alternative is now adopted as `Smart CI / Required Gate` (registered after evidence); the diff-scoped secret scan remains hosted on every PR. |
| ADR-0052 | **Retained** for its keep/fix/kill verdicts (mutation manual; k6 lanes green; release lanes tag-gated). Its "branch protection state" paragraph becomes historical once CI-03 registers the gate; nightly ownership consolidates under CI-10 without reopening the mutation schedule. |
| ADR-0051 | Unchanged — push/merge remain agent-executable once exact-head evidence and the review pipeline pass; the *required* evidence becomes the stable gate instead of convention. |

### Acceptance conditions and delegated rulings

The pack left nine conditions open; the rulings below were made under the maintainer's delegation and are recorded on CI-00 `#2324`.

| # | Condition | Ruling |
| --- | --- | --- |
| 1 | Ownership model | Personal GitHub Pro first; organization control plane deferred to CI-14 with recorded triggers |
| 2 | Control-plane placement | Base-ref `pull_request_target` planner/gate, read-only, no head execution |
| 3 | Required gate | `Smart CI / Required Gate` + `strict: true`, registered after ≥20 PRs of observation without false reds |
| 4 | Execution mode | `hosted` default; `hybrid` after CI-04 proves the runners; explicit override, no transparent fallback |
| 5 | Platform strategy | Linux semantic baseline; Windows contract on PRs; full Windows weekly + release |
| 6 | Main-push behaviour | Tree-SHA-bound landed verifier; direct push escalates |
| 7 | Selection | Shadow first; recall over ≥20 PRs; one lane family at a time |
| 8 | Nightly / mutation | Change-driven nightly + weekly sweep; mutation manual |
| 9 | Order of savings | Storage → duplicate events → platform right-sizing → self-hosted |

Before the visibility change (CI-13 `#2337`, human gate): baseline recorded; planner in shadow with the recall report; gate observed then registered with `strict: true`; landed verifier and direct-push escalation proven; actions SHA-pinned; storage under the allowance or a spend decision recorded; hosted-only rehearsal of R0/R2/R3/R4 PRs, a merge, a nightly no-change skip and a release dry-run; public-asset and review-integration billing decisions recorded; spend ceiling and alerts set. The maintainer changes visibility manually; runners are registered only afterwards.

## Alternatives Considered

- **Keep the current topology and pay overage.** Operationally simplest; measured projection puts the current topology far beyond the 3,000-minute allowance (see `docs/ci/CI_BASELINE.md`) and it fixes none of the duplication, platform or trust issues. Rejected.
- **Move everything to self-hosted runners.** Removes hosted billing but makes CI availability depend on personal machines and creates a broad persistent trust boundary for agent-authored code. Rejected; self-hosted is an isolated execution plane for trusted heavy work only.
- **Organization-owned control plane now (GitHub Team).** The strongest boundary (ruleset-required workflow outside the PR write boundary; runner group restricted to one trusted workflow). Deferred by maintainer directive; recorded as CI-14 with the triggers that reopen it.
- **Another CI provider.** Trades a mature repository-specific topology for migration cost and different quotas without fixing selection or trust. Rejected.
- **Path filters only.** Cheap and unsound for cross-cutting dependencies (a Domain DTO change reaches API, MCP, export/import and the frontend). Path groups seed the planner; the ownership/impact map is the selection basis.
- **Kubernetes / Actions Runner Controller.** A control plane larger than the workload for two machines. Rejected; revisit only with measured concurrent demand.
- **Retry-to-green for flaky lanes.** Hides defects. Rejected; a rerun may classify a flake but never erases the first failure (CI-15 `#2339`).

## Consequences

- **Positive.** Hosted minutes become a bounded clean-control budget; ordinary PR critical paths and aggregate compute fall without deleting assurance indiscriminately; Windows is tested for what actually differs; branch protection depends on one stable semantic gate; every skip has a recorded reason; self-hosted compromise has a small blast radius; the private cutover has a rehearsed, reversible path.
- **Negative.** Planner and policy correctness become a new R4-class subsystem; savings wait on a shadow period; two VMs must be patched, monitored and reset; runner mode is explicit rather than automatic; the personal-mode check-name residual exists until CI-14; test taxonomy and harness repair are real engineering work.
- **Neutral.** GitHub Actions stays the user-facing CI; existing reusable workflows and proving checks stay valid; release and security lanes continue under one policy.
- **Not changed by this ADR.** Repository visibility, billing, branch protection, runner registration, secrets, environments — maintainer actions, recorded when performed.

## References

- Tracker CI-00 `#2324`; children CI-01..CI-15 `#2325`–`#2339`.
- `docs/ci/SMART_CI.md` (architecture and operating model), `docs/ci/RUNNER_TOPOLOGY_AND_THREAT_MODEL.md`, `docs/ci/PRIVATE_REPO_CUTOVER_CHECKLIST.md`, `docs/ci/CI_BASELINE.md`.
- `docs/analysis/2026-08-30-smart-ci/RECONCILIATION.md` — pack-versus-repository corrections; the pack as received beside it.
- GitHub Docs (read 2026-08-30): Actions runner pricing; GitHub Actions billing (per-job rounding, OS multipliers, Pro allowances); Self-hosted runners reference (queued jobs when offline; ephemeral runners); Secure use reference (self-hosted runner risk); Managing auto-merge; Available rules for rulesets (organization scope of required workflows).
