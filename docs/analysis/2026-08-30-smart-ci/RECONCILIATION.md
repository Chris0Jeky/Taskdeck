# Smart CI pack — reconciliation against the live repository (2026-08-30)

Last Updated: 2026-08-30

**What this is.** The maintainer handed the agent pass a Smart CI and private-repository readiness
pack (the files beside this record, suffixed `_AS_RECEIVED`) with the instruction to inspect it,
unbundle it, update docs, seed issues and start scaffolding — under one constraint the pack did not
assume: **the repository goes private for the v0.3.0 release on a personal GitHub Pro account, with
no Team/Enterprise dependency initially**. This record lists every place the pack and the live
repository disagree, and what was done with each. Decision text is ADR-0066; the tracker is CI-00
`#2324`; the human-only actions are `OUTSTANDING_TASKS.md` §J.

## 1. Corrections to the pack's facts (live reads, 2026-08-30)

| # | Pack claim | Live state | Disposition |
| --- | --- | --- | --- |
| 1 | Branch protection "could not be read through the connector"; latest record ADR-0052 | Read live: classic protection on `main`, required contexts exactly the three security scans, `strict: false`, `enforce_admins: false`, `required_approving_review_count: 0`, force-push and deletion blocked, no rulesets; repository `allow_auto_merge: false`, squash disabled, merge commit + rebase allowed | ADR-0052's record is confirmed verbatim; CI-03 registers the stable gate + `strict: true`; auto-merge stays a maintainer choice after registration |
| 2 | Runner-group / ruleset-required workflow on an organization is the preferred boundary | Maintainer directive: personal Pro account first | Ruling 1 on CI-00; organization upgrade deferred to CI-14 `#2338` with recorded triggers; personal-mode control plane = base-ref `pull_request_target` planner/gate (ruling 2) |
| 3 | Run counts: 12,560 lifetime, 2,647 since 2026-08-01, 1,635 PR-triggered | Re-measured over 2026-07-31..2026-08-30 by `scripts/ci/smart-ci/measure-ci-estate.mjs` — see `docs/ci/CI_BASELINE.md`; the lifetime figure was not re-measured | The baseline replaces the snapshot; PR #2280 timings are kept only as the pack's historical sample |
| 4 | Required lane job list | Matches, plus one job the pack missed: `Docs Governance / Worktree Helper (Windows PowerShell)` — a ~5.7-minute **Windows** job inside docs governance | Counted in the baseline; a candidate for the CI-07 Windows contract (it *is* a Windows contract) |
| 5 | Storage not measured | Actions cache 10,734,063,833 bytes / 179 caches (at the 10 GB cap); 31,942 artifacts on record | Ruling 9: storage saving first (CI-09 `#2333`); artifact bytes measured in the baseline |
| 6 | "Pin external actions to full SHAs" | Only `gitleaks/gitleaks-action` is SHA-pinned; 15 other external actions use tags; `sha_pinning_required: false`; default workflow token already `read` | CI-11 `#2335`; the inventory tool ships with the scaffold |
| 7 | Self-hosted runners assumed absent | Confirmed: 0 runners | CI-04 prepares; registration only after CI-13 |
| 8 | `ci-release.yml` may qualify the same commit twice (tag push + release published) | Confirmed triggers: `push: tags: v*`, `release: published`, `workflow_dispatch` | Measured and collapsed under CI-10 `#2334` |
| 9 | Deep-quality ownership fragmented | Confirmed; live symptoms `#2180` (nightly mobile-safari red since 2026-08-24) and `#1210` (`ci-extended` `startup_failure`) | CI-10 owns both |
| 10 | Merge queue unavailable on personal private repos | Correct (Enterprise Cloud only); `merge_group` in `ci-required.yml` is inert for this repository | Ruling 3: branch-current (`strict`) replaces merge queue; the trigger stays harmless |
| 11 | Copilot code review consumes Actions minutes in private repos | Repository has the Copilot review workflows active (`dynamic/copilot-pull-request-reviewer`) and the Codex GitHub App | CI-13 A: verify private-mode billing of both before cutover; adjust review cadence to after-CI-stabilises |
| 12 | Not in the pack | CodeQL default setup was disabled 2026-08-19 (`#1819`); on a **private** repository CodeQL and `actions/dependency-review-action` require GitHub Advanced Security, which a personal Pro plan does not include | CI-11 records Semgrep as the SAST gate and the in-repo dependency signals (`reusable-dependency-security-signals.yml`) as the dependency gate; `ci-extended`'s `dependency-review` job must be removed or gated before cutover (CI-13 A) |
| 13 | Not in the pack | GitHub Pages (`pages-frontend.yml`) keeps working from a private repository on Pro, but the published site stays public | CI-13 A public-asset review |
| 14 | Pack's CI-15 seed titled "CI-14:" | Typo | Seeded as CI-15 `#2339` |
| 15 | Example workflows reference `ORGANIZATION/taskdeck-ci-policy` | Not applicable in personal mode | Archived as received; the personal-mode shadow workflow is written fresh (CI-02 scaffold) |
| 16 | Example policy lanes (`linux-semantic`, `affected-integration`, …) | The repository's real lanes are its reusable workflows and their jobs | The checked-in policy maps lanes to the actual reusable workflow jobs so the gate can verify real check names |

## 2. Rulings taken under delegation

See ADR-0066 §Acceptance conditions and CI-00 `#2324` — nine rulings (ownership, control-plane
placement, gate, execution mode, platform strategy, main-push, selection, nightly/mutation, order of
savings). Overturning any is one reply on `#2324`.

## 3. Issue graph

| Pack seed | Repository issue | Change from the seed |
| --- | --- | --- |
| CI-00 | `#2324` | Adds the maintainer directive, the nine rulings, the personal-mode ordering |
| CI-01 | `#2325` | Names the tool and the committed baseline path |
| CI-02 | `#2326` | Base-ref `pull_request_target` placement; recall report named |
| CI-03 | `#2327` | Tree-SHA landed verifier; maintainer settings listed explicitly |
| CI-04 | `#2328` | Personal-mode only; registration after cutover; never on a public repo |
| CI-05 | `#2329` | Folds `#1639` |
| CI-06 | `#2330` | Relates `#1512`, `#1521` |
| CI-07 | `#2331` | Folds `#2157`/`#2159`/`#2161` (with CI-15) |
| CI-08 | `#2332` | Folds `#1872`, `#2312` |
| CI-09 | `#2333` | Live storage numbers; **first saving** |
| CI-10 | `#2334` | Folds `#1210`, `#2180` |
| CI-11 | `#2335` | Live pin inventory; GHAS limits; `#1819` decision |
| CI-12 | `#2336` | Unchanged in scope |
| CI-13 | `#2337` | Executable checklist for Pro; nine ordered maintainer actions |
| CI-14 | `#2338` | Reframed as a **deferred decision** with triggers |
| CI-15 | `#2339` | Title fixed; first quarantine cohort named |

Related issues re-pointed with a comment: `#1872`, `#2157`, `#2159`, `#2161`, `#1210`, `#1819`,
`#2149`, `#2312`, `#1639`, `#2180`.

## 4. What the scaffold PRs land (behaviour-preserving)

- Decision + docs: ADR-0066, `docs/ci/` (architecture, runner threat model, cutover checklist,
  baseline), this record, STATUS / MASTERPLAN / REVIVAL_PLAN / OUTSTANDING_TASKS / TESTING_GUIDE /
  AGENT_INDEX updates, the measurement tool and the committed baseline.
- Control-plane scaffold (CI-02/CI-03/CI-11 seeds): `ci/policy.v1.json` + schemas, the deterministic
  planner and gate evaluator with fixtures, `smart-ci-shadow.yml` (planner from the base ref on
  `pull_request_target`, planner self-tests on `pull_request`, observation-mode
  `Smart CI / Required Gate`), the action-pin inventory, the runner broker skeleton and runbook.
- Nothing in either PR changes `ci-required.yml` job selection, branch protection, visibility,
  billing, runners or secrets.

## 5. Declined or deferred

| Item | Why |
| --- | --- |
| Copying the pack's example workflows | They assume an organization control repository; personal mode needs the base-ref pattern instead |
| Opening the org/Team decision as a v0.3 item | Maintainer directive says personal first; CI-14 records the triggers |
| Running the one-time artifact cleanup | Deletion is a maintainer-authorized action; CI-09 prepares the dry-run command |
| Enabling any selection now | Shadow evidence first (ruling 7) |
