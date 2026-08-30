# Taskdeck CI Audit — 2026-08-30

## Scope and limits

This audit inspected the live workflow topology, reusable workflows, recent run history, PR #2280's exact-head CI jobs and timing artifacts, the current CI ADRs, issue #1173, and the repository ruleset collection. The repository was changing rapidly on 2026-08-30; an implementation pass must refresh every measurement before editing workflows.

The audit did not change GitHub settings and could not read the live classic branch-protection endpoint through the connector. The most recent in-repository branch-protection record is ADR-0052, dated 2026-08-19.

## Executive findings

1. **The suite is rigorous but the event topology is expensive.** `ci-required.yml` runs on pull requests, pushes to `main`/`master`, and merge-group events. It invokes broad Linux and Windows matrices, containers, security scanners, migrations, and E2E.
2. **The branch-protection contract is weaker than the workflow name implies.** ADR-0052 records that only three security contexts were required, `strict` was false, and the overall CI workflow was gated by convention rather than branch protection. The repository currently has no rulesets.
3. **Windows is used as a second semantic platform rather than a compatibility platform.** Full backend unit, API integration, and frontend validation execute on both Linux and Windows.
4. **Several short hosted checks are separate jobs.** In a private repository, per-job minute rounding makes a collection of seconds-long jobs cost more than one compatible hosted control job. Consolidation must respect permission and isolation boundaries, but the current topology has measurable rounding overhead.
5. **The API suite is the largest measured operating-system asymmetry.** In PR #2280, API integration took roughly 6 minutes on Ubuntu and 15.5 minutes on Windows. Several MCP HTTP/process tests were approximately two to three times slower on Windows.
6. **The frontend Windows lane duplicates work that is mostly platform-independent.** It performs launcher tests, `npm ci`, lint, type checking, production build, and full coverage. Only the launcher/process/path portion has a strong reason to be Windows-specific.
7. **Deep-quality ownership is fragmented.** `CI Extended`, `CI Nightly`, `Nightly Quality Signals`, release workflows, and manual mutation testing partially overlap without one machine-readable policy describing why each check exists or when it should run.
8. **A personal repository cannot harden self-hosted access with organization runner-group workflow restrictions.** A PR-controlled workflow can attempt to target a repository-level runner. The strongest path is organization ownership, a runner group limited to one trusted workflow on `main`, and an organization ruleset-required workflow outside the product PR write boundary. The personal fallback must remain no-secret, isolated, owner-only, and preferably ephemeral.
9. **The repository has unusually high CI frequency.** The Actions API reported 12,560 lifetime workflow runs, 2,647 runs since 2026-08-01, and 1,635 pull-request-triggered workflow runs in that same partial month.
10. **Automated review may have a private-mode Actions cost surface.** GitHub documents that Copilot code review consumes Actions minutes in private repositories. The Taskdeck review integration must be measured after cutover rather than assumed free.
11. **The current reusable-workflow decomposition is an asset.** The redesign should preserve and parameterize it rather than migrate to another CI provider or rewrite everything at once.

## Current required path

The required workflow currently contains, directly or through reusable workflows:

```text
Docs governance
Release-workflow contract
Backend architecture
Backend unit: Ubuntu + Windows
API integration: Ubuntu + Windows
Migration validation
Frontend validation: Ubuntu + Windows
Paper color audit
Container image builds
Gitleaks
Dependency security
Semgrep
E2E smoke after the main semantic jobs
```

The full workflow runs on:

```yaml
push:
  branches: [main, master]
pull_request:
merge_group:
```

This creates three forms of duplication:

- a pull request is qualified;
- the same or almost identical state is qualified again around merge-queue semantics where available;
- the landed commit on `main` is qualified again.

For a private repository under a personal GitHub account, merge queue should not be a design dependency. The relevant private-repository flow is a synthetic PR merge commit, a branch-up-to-date requirement, required status checks, and optional auto-merge.

## Measured PR #2280 sample

The following values are approximate wall-clock job or test-step durations from exact-head run `33295882109`:

| Lane | Platform | Approximate duration |
|---|---:|---:|
| Backend unit | Ubuntu | 5.2 min |
| Backend unit | Windows | 6.1 min |
| API integration tests | Ubuntu | 6.0 min test step; 7.3 min job |
| API integration tests | Windows | 15.5 min test step; 16.8 min job |
| Frontend validation | Windows | 12.5 min job |
| Frontend validation | Ubuntu | additional full lane; same semantic frontend checks plus bundle gate |
| Gitleaks | Ubuntu | seconds |
| Semgrep | Ubuntu | about one minute |

The five directly measured duplicated semantic jobs—backend Linux/Windows, API Linux/Windows, and frontend Windows—already total about 47.8 runner-minutes. That excludes frontend Linux, E2E, containers, migration validation, architecture, docs, and scanners.

### API hotspots

The timing artifacts show meaningful fixed-overhead/process asymmetry. Representative classes:

| Test class | Ubuntu | Windows |
|---|---:|---:|
| `AutomatedJourneyGapTests` | ~74 s | ~98 s |
| `McpHttpAuthTests` | ~33 s | ~92 s |
| `McpHttpIsolationTests` | ~29 s | ~80 s |
| `McpHttpProposalSurfaceTests` | ~28 s | ~78 s |
| `McpHttpHostileWriteAuthTests` | ~26 s | ~76 s |
| `ChatSseEndpointTests` | ~22 s | ~49 s |
| `FirstRunMcpSmokeTests` | ~17 s | ~44 s |
| `McpHttpBackgroundServiceTests` | lower on Linux | ~43 s on Windows |

This points to process startup, readiness polling, fixed delays, shutdown, and repeated fixture creation as high-value engineering targets.

## Existing decisions to preserve or amend

### ADR-0013

Reusable-workflow decomposition remains correct. The redesign should add runner selection, test-plan inputs, and a stable aggregate gate without discarding reusable workflows.

### ADR-0035

Secret/dependency/SAST checks belong in the merge-control story. The redesign should distinguish:

- lightweight diff/control checks on every pull request;
- full-repository scans on changed dependencies, nightly, and release;
- clean hosted execution for workflow/security-control changes.

### ADR-0052

ADR-0052 correctly removed a wasteful mutation schedule and recorded per-lane keep/fix/kill verdicts. It did **not** solve private-repository economics, duplicate event coverage, risk-based selection, self-hosted trust, or stable aggregate branch protection. It should be amended or superseded narrowly, not treated as wrong.

## Structural problems

### 1. No canonical CI plan

Eligibility logic exists in `ci-extended`, but the required lane itself has no versioned plan describing:

- changed components;
- risk level;
- trust class;
- selected suites;
- selected operating systems;
- runner class;
- reasons for every selection and skip.

### 2. Required check instability

Branch protection should not be forced to track a large list of inner reusable-workflow context names. A stable aggregate check should evaluate the plan and the outcomes.

### 3. Test selection is path-oriented, not dependency-oriented

A path filter answers “which directory changed,” not “which behavior can be affected.” Taskdeck needs a conservative impact graph connecting production modules, cross-cutting contracts, migrations, APIs, MCP, frontend, packaging, and release surfaces.

### 4. Platform matrix is not risk-calibrated

Most .NET domain/application semantics and TypeScript/Vue semantics do not need identical full-suite repetition on both operating systems for every change. Platform-specific contracts do.

### 5. Self-hosted execution would currently inherit too much trust

An ordinary persistent runner installed directly on the development machine would expose caches, files, processes, tokens, package hooks, and potentially the normal development environment to repository code. Private repository status does not make arbitrary agent-written code trustworthy.

### 6. No CI economics feedback loop

There is no first-class ledger for aggregate runner-minutes, hosted cost estimate, self-hosted compute, queue delay, test yield, flake rate, slow-test regressions, redundant exact-SHA runs, cache utility, or artifact retention.

## Highest-value changes

1. Add `Smart CI / Required Gate` and actually require it.
2. Remove full `main` duplication after PR qualification.
3. Make Linux the semantic baseline and Windows a targeted compatibility contract on ordinary PRs.
4. Introduce a conservative policy planner with fail-closed full escalation.
5. Repair/shard slow MCP/process API tests.
6. Move heavy trusted work to isolated, on-demand self-hosted execution.
7. Consolidate nightly quality ownership and skip heavy nightlies when `main` has not changed.
8. Pin external actions to full commit SHAs and force CI-control changes through hosted clean-room validation.
9. Publish a local CI evidence ledger and budget report.
