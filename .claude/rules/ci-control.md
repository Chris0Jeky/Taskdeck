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
- **A green control-plane PR is not a mergeable one.** Under the ADR-0066 amendment of 2026-09-03, a
  change to a `ci/policy.v1.json` control path merges only after **the maintainer's own review plus one
  fresh-context review**. Passing `ci-required` satisfies the evidence gate, not the authority gate.
  A batch is delegated only by an explicit ruling naming its PRs, as on 2026-09-06 (twelve named PRs)
  and 2026-09-09 (four named PRs); neither generalises. Open the PR ready-for-review, record it on
  `OUTSTANDING_TASKS.md` §J.2, and read §J.3 first: whether this rule should stay as written is an open
  question there, raised because it has now been merged past three times (`#2772` and `#2787` on
  2026-09-08, the CI-continuation train on 2026-09-10). Until §J.3 (b) is answered the rule stands.
  If you write "do not auto-merge" in your own PR body, that is a promise to the next reader; do not
  merge past it without a ruling that names the PR.
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
