---
paths:
  - ".github/**"
  - "ci/**"
  - "scripts/ci/**"
---

# CI-control region

You are editing CI control: workflows, the Smart CI planner/policy, or CI scripts. This is the one region
no `CLAUDE.md` covers by directory, so these rules load by path.

- **Risk class R4** (ADR-0066 Smart CI Fabric, tracker CI-00 `#2324`). CI-control changes qualify
  **hosted-only**: the proving check is the hosted run on the exact PR head, never a local approximation.
  Local checks are additive.
- **`ci-required.yml` is the required merge gate.** CI Extended is an optional, non-blocking lane
  (several jobs are label-gated). Read its results; it does not gate the merge.
- **`smart-ci-shadow.yml` is observation-only** (ADR-0066, CI-02 `#2326`) until the maintainer registers
  `Smart CI / Required Gate` as a required check. A red shadow gate is a planner/policy defect to fix
  under R4, not a product verdict — never ignore it, never call it flaky (global law 1).
- **Planner/policy tests:** `node --test scripts/ci/smart-ci/*.test.mjs`. `ci/policy.v1.json` maps
  path patterns to risk floors and lanes; a pattern change needs a test that exercises it.
- **Docs-governance job** (`reusable-docs-governance.yml`) also runs the PowerShell harness tests
  (`scripts/git/Test-New-CodexIssueWorktree.ps1`, `Invoke-TaskdeckReadOnlyInventory.ps1 -SelfTest`,
  `scripts/agentic/Test-Assert-TaskdeckCheckoutFingerprint.ps1`). Adding a harness script means adding
  its test here.
- **DCO verifier assets under `scripts/ci/` are dormant** (enforcement paused 2026-08-23, `#2019`). Do not
  reactivate them from a CI change; that needs its own decision.
- `gh run rerun` reuses the OLD merge ref — use `gh pr update-branch` to re-prove against a moved base.
  Rerunning an older `main` run cancels the newer tip's run.
- Reference: `docs/ci/SMART_CI.md`, `docs/TESTING_GUIDE.md`.
