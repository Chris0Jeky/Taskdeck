# Prompt for the Taskdeck repository agent

You are working inside `Chris0Jeky/Taskdeck` on the CI architecture required to make the repository private without weakening verification or wasting compute.

The attached bundle is a **proposal and evidence pack**, not authority over the live repository. Reconcile it against the current branch, current GitHub issues/milestones, current Actions history, and repository governance before acting.

## Goal

Turn Taskdeck's CI into a measured, policy-driven Smart CI Fabric:

```text
change inventory
→ trust classification
→ risk/impact plan
→ selected verification graph
→ hosted/self-hosted execution
→ evidence aggregation
→ one stable required gate
```

The intended result is:

- GitHub Actions remains the control plane.
- Pull requests carry substantive qualification.
- Normal pushes to `main` run only a bounded landed verifier; direct/bypass pushes escalate.
- Linux is the primary semantic platform.
- Windows runs a targeted compatibility contract on ordinary PRs and a full regression weekly/release.
- Heavy trusted work can use isolated, on-demand self-hosted runners.
- Preferred hard boundary: organization runner group restricted to one trusted reusable workflow on `refs/heads/main`, plus an organization ruleset-required workflow that a Taskdeck PR cannot edit.
- Workflow/release/security-control changes and untrusted inputs stay GitHub-hosted.
- Nightly work is change-driven, with a weekly full sweep.
- One stable `Smart CI / Required Gate` is the branch-protection contract.
- Compatible seconds-long hosted checks are coalesced to reduce private-repository per-job minute rounding, without merging distinct permission/trust boundaries.
- Every selection, skip, cost, duration, and result is inspectable.

## Read first

Read and obey the repository's authority files, then at minimum inspect:

- `AGENTS.md`
- `.codex/memories/00_ACTIVE.md`
- `OUTSTANDING_TASKS.md`
- `docs/decisions/ADR-0013-ci-topology-reusable-workflows.md`
- `docs/decisions/ADR-0035-required-security-scan-merge-gate.md`
- `docs/decisions/ADR-0052-ci-estate-right-sizing.md`
- `docs/strategy/PRODUCT_DIRECTION.md`
- `docs/REVIVAL_PLAN.md`
- `docs/TESTING_GUIDE.md`
- `.github/workflows/ci-required.yml`
- `.github/workflows/ci-extended.yml`
- `.github/workflows/ci-nightly.yml`
- `.github/workflows/nightly-quality.yml`
- `.github/workflows/ci-release.yml`
- `.github/workflows/release-security.yml`
- all reusable workflows and `scripts/ci/**`
- issues `#1173`, `#1275`, `#2237`, and any newer CI/private-repository issue

Inspect live GitHub Actions runs and settings evidence. Do not assume the measurements in the bundle remain current.

## Hard safety boundaries

Do not, without an explicit maintainer instruction in the current issue/session:

- make the repository private or public;
- change billing, budgets, payment methods, or account plan;
- create/transfer a GitHub organization or transfer repository ownership;
- create/register/remove a self-hosted runner or runner group;
- use or expose runner registration tokens;
- change branch protection/rulesets;
- create secrets, environments, deployment credentials, or signing keys;
- publish releases/packages/pages;
- let a modified workflow select itself onto a privileged self-hosted runner;
- weaken a required gate merely to make a PR green.

You may prepare exact maintainer checklists and commands with placeholders. Never commit tokens, machine identifiers, private paths, credentials, or personal data.

## Required first pass

1. Re-measure the last 30 days and a representative exact-head PR:
   - workflow/job count;
   - hosted operating-system minutes;
   - aggregate runner-minutes and critical path;
   - PR/main/merge-group duplication;
   - reruns/flakes;
   - artifact/cache storage;
   - lane failure yield;
   - top slow test classes.
2. Verify current branch protection/rulesets, required contexts, repository ownership constraints, Actions workflow-execution protections, and whether runner-group selected-workflow restrictions are available. If an API/setting is unavailable, record it as human-verified rather than guessing.
3. Reconcile the bundle's findings with current code. Record every correction.
4. Decide whether to amend ADR-0013/0052 or create the next ADR. Do not reuse a claimed ADR number.
5. Create one tracker and bounded child issues. Reuse or re-scope existing issues where they already own the work.

## Implementation authority

Go as far as possible on **behavior-preserving, default-off scaffolding**:

- CI measurement/report script;
- policy JSON and schema;
- deterministic planner with fixtures;
- shadow-mode plan artifact/summary;
- aggregate gate implementation in non-required mode;
- test ownership/impact manifest;
- runner labels and example configuration docs;
- action pinning inventory;
- issue/ADR/docs reconciliation.

Do not begin test skipping or remove existing qualification until shadow evidence proves planner recall and the stable gate has a safe migration path.

## Engineering rules

- Unknown or unmapped change must select the conservative full plan.
- Planner output must be deterministic, schema-validated, exact-SHA-bound, and content-free.
- Required workflows must always report; do not use top-level path filters that leave required checks pending/missing.
- Keep reusable workflows; parameterize runner and selected shard rather than duplicating logic.
- Treat CI/workflow/policy/release/security-control changes as the highest trust class and run their validation GitHub-hosted.
- Do not claim that runner labels or an `if` expression prevent a PR from targeting a repository-level self-hosted runner. Prefer runner-group selected-workflow restriction. In the personal fallback, document the weaker boundary and keep runners no-secret, isolated, and owner-only.
- A trusted reusable workflow must independently revalidate the immutable PR/head/merge SHA and control-path diff before executing proposed code.
- Ordinary self-hosted jobs must have no repository/environment/release secrets and read-only token permissions.
- Do not claim transparent self-hosted-to-hosted fallback. Use an explicit execution mode or override.
- Preserve a manual hosted full-qualification workflow.
- Release artifacts must be rebuilt from the tag in a clean context.
- A retry may diagnose a flake but may not silently erase the original failure.

## Specific architecture corrections to seed

- Stable aggregate check: `Smart CI / Required Gate`.
- Branch-current/automerge design for private personal operation; do not depend on merge queue.
- Linux semantic baseline and bounded Windows compatibility contract.
- API behavioral shards and Windows MCP/process fixed-overhead repair.
- One frontend semantic lane; Windows launcher/platform subset.
- Journey-aware E2E and risk-gated container builds.
- Change-driven nightly with weekly full entropy sweep.
- Full SHA pinning of external Actions.
- CI receipts and weekly budget/flake/slow-test report.
- Isolated persistent VM runners first; ephemeral snapshot path later.

## Verification

For every PR:

- run workflow syntax/actionlint checks;
- run planner/policy/schema fixture tests;
- prove fail-closed behavior;
- prove exact-SHA/policy binding;
- prove selected and allowed-skipped outcomes at the aggregate gate;
- keep current full CI green during shadow mode;
- use a fresh-context adversarial review for security/trust changes;
- record exact-head CI evidence.

## Deliverable

Return:

1. the live-state audit and corrected recommendation;
2. ADR/tracker/child issue map;
3. merged behavior-preserving scaffolding, where safe;
4. what remains human-owned;
5. exact next execution order;
6. measured before/after evidence;
7. residual risks and rollback path.

Progress is merged, verified improvements and a safer private-cutover path, not merely creating a large backlog.
