---
paths:
  - ".github/**"
  - ".github/workflows/**"
  - ".github/actions/**"
  - ".github/dependabot.yml"
  - ".github/release.yml"
  - ".github/CODEOWNERS"
  - ".github/actionlint.yaml"
  - ".github/actionlint.yml"
  - ".github/settings.yml"
  - "CODEOWNERS"
  - ".config/dotnet-tools.json"
  - "nuget.config"
  - "NuGet.Config"
  - "backend/nuget.config"
  - "backend/Directory.Build.targets"
  - "package.json"
  - "package-lock.json"
  - ".npmrc"
  - "**/.npmrc"
  - "frontend/package-lock.json"
  - "frontend/taskdeck-web/.nvmrc"
  - ".nvmrc"
  - "ci/**"
  - "scripts/ci/**"
  - "scripts/build-release.ps1"
  - "scripts/build-release.sh"
  - "scripts/deploy/**"
  - "scripts/security/**"
  - "global.json"
  - "backend/Directory.Build.props"
  - "backend/Directory.Packages.props"
  - "frontend/taskdeck-web/package.json"
  - "frontend/taskdeck-web/package-lock.json"
  - ".gitleaks.toml"
  - ".gitleaksignore"
  - ".semgrepignore"
  - ".semgrep/**"
---

# CI-control region

You are editing a **control path**: a workflow, the Smart CI planner/policy, a CI script, a deploy or
security script, a release build script, or a dependency manifest. No `CLAUDE.md` covers this region by
directory, so these rules load by path, and the `paths:` list above mirrors `ci/policy.v1.json`'s
`controlPaths` entry for entry. **`ci/policy.v1.json` is the authority; this frontmatter is a mirror.**
The one deliberate addition is `.github/**`, kept from this file's earlier scope so an edit to something
like `.github/ISSUE_TEMPLATE/**` still loads the region's proving and review guidance even though policy
does not class it as a control path. Everything else is a mirror, not a judgement.
If you add a control path there, add it here in the same PR, or this rule silently stops loading for it —
which is how `#2866` came to add 15 lines to `scripts/deploy/audio-response-policies.test.mjs`, a declared
control path, with nothing telling the agent it had just made an R4 change.

- **Risk class R4** (ADR-0066 Smart CI Fabric, tracker CI-00 `#2324`). CI-control changes qualify
  **hosted-only**: the proving check is the hosted run on the exact PR head, never a local approximation.
  Local checks are additive.
- **A green control-plane PR is not a mergeable one.** Under the ADR-0066 amendment of 2026-09-03, a
  change to a `ci/policy.v1.json` control path merges only after **the maintainer's own review plus one
  fresh-context review**. Passing `ci-required` satisfies the evidence gate, not the authority gate.
  A batch is delegated only by an explicit ruling naming its PRs, as on 2026-09-06 (twelve named PRs)
  and 2026-09-09 (four named PRs); neither generalises. Open the PR ready-for-review, record it on
  `OUTSTANDING_TASKS.md` §J.2, and read §J.3 first: whether this rule should stay as written is an open
  question there. It is open because the rule keeps not holding - three disclosures still awaiting a
  reply (`#2772` and `#2787` on 2026-09-08, the CI-continuation train on 2026-09-10), on top of five
  earlier post-hoc merges (`#2479`, `#2529`, `#2548`, `#2549`, `#2556`) that the maintainer
  acknowledged without revert on 2026-09-06 (q-2 = A). Until §J.3 (b) is answered the rule stands.
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
