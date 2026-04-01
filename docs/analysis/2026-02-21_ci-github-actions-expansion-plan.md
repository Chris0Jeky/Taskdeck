# CI and GitHub Actions Expansion Plan

Date: 2026-02-21  
Scope: Taskdeck CI/workflow expansion strategy grounded in current repo state and seeded backlog  
Authoring context: Local workflow/config scan + GitHub issue reconciliation + targeted GitHub docs grounding

## Purpose

Define a pragmatic expansion strgithub/workflows/ci-required.yml` so the pipeline scales with the already-seeded testing/security/ops issues without turning PR feedback into a bottleneck.

This plan is non-authoritative by itself. `docs/STATUS.md` and `docs/IMPLEMENTATION_MASTERPLAN.md` remain the canonical sources of truth.

## Current-State Snapshot

Current CI workflow (`.github/workflows/ci-required.yml`) is strong and already above baseline:

- Single workflow with clear job decomposition:
  - docs governance
  - backend architecture tests
  - backend unit matrix (`ubuntu-latest`, `windows-latest`)
  - API integration matrix (`ubuntu-latest`, `windows-latest`)
  - frontend unit matrix (`ubuntu-latest`, `windows-latest`)
  - container image validation/build/export
  - Playwright smoke (`ubuntu-latest`)
- Concurrency cancellation enabled at workflow level.
- Cache posture present for NuGet, npm, and Playwright browsers.
- Failure artifacts are uploaded for key lanes.

Approximate execution shape per run:

- 7 logical jobs
- 10 job instances when matrix expansion is applied

## Key Gaps to Address Before Backlog Growth

1. Trigger model is PR/push only; no scheduled/nightly lanes for heavy suites (load, mutation, fuzz, broad E2E matrix).
2. Workflow is monolithic; duplication pressure will rise as seeded test/security/release lanes are added.
3. No explicit `merge_group` trigger, which becomes important if merge queue is enabled.
4. No dedicated PR dependency-review security gate yet.
5. No explicit workflow lint/static validation lane for Actions YAML drift.
6. Path-aware execution strategy is not formalized; runtime/cost will grow as suites are added.

## Expansion Strategy (Dependency-Aware)

### Track A: Required PR Gates (fast and deterministic)

Target outcome:
- keep required checks under a predictable budget while preserving current quality bar.

Actions:
- Keep existing required lanes (docs governance, backend architecture, backend unit/integration, frontend unit, smoke E2E).
- Add `merge_group` trigger parity for required checks.
- Add dedicated PR dependency review job (`actions/dependency-review-action`) for lockfile/dependency changes.
- Add workflow lint lane (`actionlint` or equivalent) for `.github/workflows/**` changes.

Backlog ties:
- `#106`, `#148` (dependency security policy/automation)
- `#151` (analysis follow-through umbrella)

### Track B: Non-blocking Extended PR Lanes (path- and label-aware)

Target outcome:
- run expensive suites only when signal suggests value, without blocking all PRs.

Actions:
- Introduce optional/conditional lanes for:
  - expanded browser matrix (`#87`)
  - visual regression (`#88`)
  - deployment hardening verification (`#142`)
- Use a stable dispatch model (`workflow_dispatch`) for maintainer-forced runs and release-candidate sweeps.
- Keep required checks unskippable to avoid branch protection ambiguity.

Backlog ties:
- `#87`, `#88`, `#142`

### Track C: Nightly and Scheduled Regression Lanes

Target outcome:
- shift high-runtime/low-frequency checks out of the critical PR path.

Actions:
- Add scheduled workflows for:
  - load/concurrency harness (`#70`)
  - fuzz/property pilot (`#89`)
  - mutation pilot (`#90`)
  - failure-injection drills (`#149`)
- Upload deterministic artifacts and summaries for triage.

Backlog ties:
- `#70`, `#89`, `#90`, `#149`

### Track D: Release Security and Provenance Lanes

Target outcome:
- ship attestable artifacts with explicit dependency/provenance posture.

Actions:
- Add release workflow path for SBOM/provenance generation and retention policy (`#103`).
- Integrate dependency/security gates from `#106`/`#148` into release readiness checks.
- Scope elevated permissions to release-only jobs and keep PR jobs least-privileged.

Backlog ties:
- `#103`, `#106`, `#148`

### Track E: Workflow Topology Refactor (reusable workflows)

Target outcome:
- reduce YAML drift and make growth manageable.

Actions:
- Split monolithic workflow into reusable called workflows for backend tests, frontend tests, container build, and E2E.
- Keep one orchestrator workflow for required checks and separate orchestrators for nightly/release tracks.

Backlog ties:
- New umbrella issue created from this plan (see GitHub mapping section).

## Recommended Workflow Topology (Target)

1. `ci-required.yml`
- PR/push/merge_group.
- Fast deterministic required checks only.

2. `ci-extended.yml`
- PR opt-in (labels/manual dispatch) for cross-browser/visual/deployment-hardening lanes.

3. `ci-nightly.yml`
- Scheduled load/fuzz/mutation/failure-injection lanes.

4. `release-security.yml`
- Tag/release triggers for SBOM/provenance and hardened artifact policy checks.

5. `reusable/*.yml`
- Shared job templates via `workflow_call` to avoid duplication and keep policy centralized.

## Governance and Safety Notes

- Keep least-privilege `permissions` per job; do not elevate globally unless required.
- Pin third-party actions to immutable references where feasible.
- If path filtering is introduced, ensure required checks cannot be left perpetually pending.
- Record lane ownership and expected runtime budgets in workflow docs/comments.

## GitHub Issue Mapping

Existing issues already covering CI-adjacent slices:

- `#70` load/concurrency harness
- `#87` cross-browser/mobile matrix
- `#88` visual regression
- `#89` property/fuzz pilot
- `#90` mutation pilot
- `#91` ephemeral integration DBs
- `#103` SBOM/provenance workflow
- `#106` dependency vulnerability policy/tooling
- `#142` deployment hardening verification matrix
- `#148` dependency update automation workflow
- `#149` failure-injection drill suite

New issue seeded from this document:
- `#168` CI workflow expansion topology and governance track

## Suggested Execution Order

1. `#168` define and land workflow topology/governance baseline
2. `#142` deployment hardening verification automation expansion
3. `#148` dependency automation + triage workflow
4. `#70` load/concurrency lane
5. `#87` and `#88` extended UI lanes
6. `#89` and `#90` scheduled deep-quality lanes
7. `#103` release SBOM/provenance lane
8. `#91` container-backed integration variant
9. `#149` deterministic failure-injection drills

## Success Metrics

- Required PR checks remain deterministic and operationally bounded.
- Nightly failures are actionable with artifacts and owner routing.
- Release workflows emit provenance/security artifacts consistently.
- Workflow duplication decreases as reusable workflow adoption increases.

## External Grounding (Primary References)

- GitHub Actions workflow syntax (events, filters, strategy, permissions):  
  https://docs.github.com/actions/reference/workflows-and-actions/workflow-syntax
- Matrix strategies for job variation and controls:  
  https://docs.github.com/actions/using-jobs/using-a-matrix-for-your-jobs
- Reusable workflows (`workflow_call`) for topology decomposition:  
  https://docs.github.com/actions/using-workflows/reusing-workflows
- Dependency review action (PR dependency risk gate):  
  https://github.com/actions/dependency-review-action
- Security hardening for GitHub Actions:  
  https://docs.github.com/actions/security-for-github-actions/security-guides/security-hardening-for-github-actions
- Merge queue trigger parity (`merge_group`) events:  
  https://docs.github.com/en/actions/reference/events-that-trigger-workflows
- Path-filter caveat for required checks (skipped workflow can leave pending checks):  
  https://docs.github.com/actions/reference/workflows-and-actions/workflow-syntax
