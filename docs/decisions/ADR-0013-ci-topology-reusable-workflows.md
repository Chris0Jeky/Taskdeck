# ADR-0013: CI Topology — Reusable Workflow Decomposition

- **Status**: Accepted
- **Date**: 2026-03 (OPS-19 wave)
- **Deciders**: Project maintainers

## Context

The original CI pipeline was a single monolithic workflow (`ci.yml`) running all checks sequentially. As the test surface grew (1668+ backend tests, 1174+ frontend tests, 24+ E2E tests, container builds, architecture checks, docs governance), the workflow became slow, hard to maintain, and difficult to extend with optional/nightly checks.

## Decision

Decompose CI into a layered topology:

**Orchestrators** (trigger-based):
- `ci-required.yml` — PR gate (must pass to merge)
- `ci-extended.yml` — opt-in extended checks (testing label / manual)
- `ci-nightly.yml` — scheduled regression + container verification
- `ci-release.yml` — release build verification + SBOM
- `release-security.yml` — dependency inventory + vulnerability reporting

**Reusable workflows** (called by orchestrators):
- `reusable-backend-unit.yml` (domain/application/CLI split)
- `reusable-frontend-unit.yml` (Ubuntu/Windows matrix)
- `reusable-api-integration.yml`
- `reusable-backend-architecture.yml`
- `reusable-docs-governance.yml`
- `reusable-e2e-smoke.yml`
- `reusable-container-images.yml`
- `reusable-backend-solution.yml` (full regression)
- `reusable-load-concurrency-harness.yml`

Each reusable workflow is independently testable and composable.

## Alternatives Considered

- **Keep monolith**: Simplest but slow, no optional lanes, no reuse across orchestrators.
- **Matrix-only split**: GitHub Actions matrix can parallelize but doesn't support different workflow compositions across triggers.
- **External CI (Jenkins/CircleCI)**: More flexibility but adds infrastructure; GitHub Actions is already integrated.

## Consequences

- **Positive**: Fast required gate (parallel lanes); optional extended checks don't block PRs; nightly catches regressions; topology is documented in workflow headers.
- **Negative**: Many workflow files to maintain; dependency ordering between lanes requires explicit `needs` declarations.
- **Neutral**: `CODEOWNERS` enforces review for workflow changes.

## References

- OPS-19 in `docs/IMPLEMENTATION_MASTERPLAN.md` (6 passes)
- `.github/workflows/` — all workflow files
