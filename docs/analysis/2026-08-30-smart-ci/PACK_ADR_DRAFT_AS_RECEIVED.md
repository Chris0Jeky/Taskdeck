# ADR-XXXX: Smart CI Fabric and Private-Repository Runner Trust

- **Status:** Proposed
- **Date:** 2026-08-30
- **Deciders:** Maintainer
- **Related:** ADR-0013, ADR-0035, ADR-0052, issue #1173, issue #1275, issue #2237, Context Fabric worker/runtime work

> The in-repository agent must allocate the next unused ADR number. This draft deliberately uses `XXXX`.

## Context

Taskdeck's reusable GitHub Actions topology is mature and rigorous, but its economics depend heavily on free public-repository hosted execution. The required workflow runs on pull requests, pushes to `main`/`master`, and merge-group events; broad Linux and Windows matrices duplicate backend, API, and frontend semantics. The repository has accumulated more than twelve thousand workflow runs, and the August 2026 run rate is high enough that a private GitHub Pro allowance cannot be treated as the primary execution budget.

Self-hosting alone is not an architecture. A persistent self-hosted runner executes repository-controlled code and can retain compromise across jobs. The repository is also heavily modified by coding agents, so same-owner commits do not automatically imply safe execution on a machine containing personal data or credentials.

Taskdeck needs to preserve its high assurance while making verification proportional to change risk, moving expensive trusted work off hosted billing, and ensuring that a private-repository cutover does not silently weaken branch protection.

## Decision

Adopt a **Smart CI Fabric** with six layers:

```text
change inventory
→ trust classifier
→ risk/impact planner
→ selected execution graph
→ evidence aggregator
→ stable required gate
```

### 1. GitHub Actions remains the control plane

Existing reusable workflows are retained and parameterized. No migration to another CI provider is authorized solely to obtain free minutes.

### 2. One stable required check

Every pull request produces `Smart CI / Required Gate`. It validates the exact SHA, policy version, selected lanes, outcomes, and allowed skips. Branch protection requires this check and requires the branch to be current before merge.

### 3. Pull requests carry substantive qualification

The full selected plan runs on `pull_request`. A normal push to `main` runs a small landed-commit verifier and escalates only when no valid PR qualification receipt exists. Private-personal operation does not depend on merge queue.

### 4. Risk and trust are separate

Risk classes R0–R4 classify potential impact. Trust classes T0–T4 classify where code may execute. CI/workflow/release/security-control changes are R4/T2 and run on GitHub-hosted clean runners even when authored by the maintainer.

### 5. Conservative change-aware selection

A versioned policy and deterministic planner select suites. Unknown paths, planner errors, stale dependency graphs, or policy mismatch select the full conservative plan. Selection begins in shadow mode and must prove recall against the existing complete suite before enforcement.

### 6. Linux is the semantic baseline

Domain, application, ordinary API, frontend, architecture, and primary E2E semantics run on Linux once. Windows pull-request coverage becomes a bounded compatibility contract for launchers, filesystem/process/desktop/MCP/SQLite/platform behavior. Full Windows regression remains weekly and release-qualified until evidence supports further reduction.

### 7. Hybrid execution

- GitHub-hosted: planner, aggregate gate, Gitleaks, workflow/control checks, untrusted contributions, CI/release changes, occasional macOS, release clean-room.
- Isolated self-hosted Linux VM: heavy trusted semantic, API, E2E, containers, deep checks.
- Isolated self-hosted Windows VM/laptop: targeted Windows compatibility and scheduled full Windows sweeps.

Self-hosted jobs carry no repository/environment secrets. The normal development environment is not a runner.

### 8. Trusted-workflow enforcement

The preferred operating model is an organization-owned private repository. Self-hosted runners sit in a runner group restricted to one selected reusable workflow on `refs/heads/main`. That trusted workflow independently validates PR identity, immutable target SHA, trust class, and control-path changes before executing proposed code.

The required merge workflow is enforced through an organization ruleset workflow sourced from a separately protected private CI-policy repository where practical. A Taskdeck product PR cannot modify or impersonate its own gate.

If the repository remains under a personal account, this hard workflow restriction is unavailable. The self-hosted path is then limited to owner-only operation with isolated/ephemeral no-secret runners and no external contributors; hosted full qualification remains the safe fallback.

### 9. Explicit runner mode

Runner selection is policy-driven through `self-hosted | hosted | hybrid` plus a maintainer override. There is no claimed transparent fallback when an offline self-hosted runner is queued.

### 10. Change-driven deep qualification

A nightly coordinator runs deep suites only when relevant `main` changes exist since the last successful deep receipt. A weekly full sweep provides entropy/platform drift coverage. Mutation remains manual/on-demand unless evidence changes ADR-0052's verdict.

### 11. CI economics and reliability are measured

Every run emits a bounded receipt with runner minutes, hosted cost estimate, queue delay, critical path, test count, failure yield, flake/rerun status, slow tests, cache utility, artifact size, and selection reasons. Policy is reviewed using these measurements.

### 12. Supply-chain hardening

External actions are pinned to full commit SHAs; CI-control changes validate on hosted runners; `pull_request_target` may not execute untrusted head code; release credentials are restricted to protected release jobs; ordinary self-hosted runners hold no signing or cloud secrets.

## Consequences

### Positive

- Private-repository hosted minutes become a bounded clean-control budget rather than the entire compute budget.
- Ordinary PR critical paths and aggregate compute fall without deleting assurance indiscriminately.
- Windows remains meaningfully tested instead of merely duplicated.
- Branch protection depends on one stable semantic gate.
- Every skip is explainable and auditable.
- Self-hosted compromise has a materially smaller blast radius.
- Deep and release coverage remain stronger than ordinary PR coverage.

### Negative

- Planner/policy correctness becomes a new critical subsystem.
- A shadow-validation period is required before savings are realized.
- Two isolated runner environments must be patched, monitored, and periodically reset.
- Full automatic hosted fallback is not available; runner mode must be explicit.
- Test taxonomy and slow-fixture repair require engineering work.

### Neutral

- GitHub Actions remains the user-facing CI system.
- Existing reusable workflows remain useful.
- Release and security lanes continue to exist but are coordinated by one policy.

## Rejected alternatives

### Keep the current topology and pay overage

Operationally simple but preserves duplicated event/platform work and does not improve CI quality engineering.

### Move everything to self-hosted runners

Reduces hosted billing but creates a broad persistent trust boundary and makes CI availability dependent on personal machines.

### Move to another CI provider

Trades a mature repository-specific topology for migration cost, different quotas, and duplicated governance without fixing test selection or trust.

### Path filters only

Cheap but unsound for cross-cutting dependencies. Path groups may seed the planner but cannot be the only impact model.

### Kubernetes/Actions Runner Controller immediately

Disproportionate for two physical machines and a solo-maintainer repository. Revisit only with measured concurrent demand or organizational runner governance.

## Acceptance conditions

Before changing repository visibility:

- [ ] Current CI cost/duration baseline is recorded.
- [ ] Planner policy/schema and fixture suite are merged in shadow mode.
- [ ] Planner recall is checked against representative full-suite outcomes.
- [ ] Stable aggregate gate is required in branch protection.
- [ ] Branch-current requirement and direct-push fallback are verified.
- [ ] Personal-account fallback versus organization-owned runner-group boundary is explicitly decided.
- [ ] If organization-owned, the runner group is restricted to the selected trusted workflow and the required workflow cannot be edited by a Taskdeck PR.
- [ ] Linux and Windows runner environments are isolated from personal data and secrets.
- [ ] Hosted override and offline-runner behavior are proven.
- [ ] CI-control changes are forced to hosted execution.
- [ ] External actions are SHA-pinned or a tracked migration exists with a hard enablement gate.
- [ ] Low-risk, high-risk, CI-control, nightly, and release-dry-run scenarios are green.
- [ ] Spending budget, alerts, cache retention, and artifact retention are explicitly configured.
- [ ] Repository visibility is changed manually by the maintainer, not by an agent.
