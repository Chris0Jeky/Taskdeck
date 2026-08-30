# scripts/ci/runners — isolated self-hosted runner tooling (CI-04 `#2328`)

Last Updated: 2026-08-30 · Threat model: `docs/ci/RUNNER_TOPOLOGY_AND_THREAT_MODEL.md` · Decision: ADR-0066 §7–9

**Nothing here registers a runner.** Registration tokens, `config.sh`/`config.cmd` runs and the
GitHub UI association are maintainer actions performed **only after the repository is private**
(`OUTSTANDING_TASKS.md` §J SC-7). While the repository is public, no self-hosted runner exists —
any fork PR could target it.

| File | Purpose |
| --- | --- |
| `Invoke-TaskdeckCiRunnerVm.ps1` | Hyper-V broker: `-Action Start` before qualifying a PR, `-Action Stop` after, `-Action Status` any time. No GitHub calls, no tokens, `-WhatIf` supported. |
| (CI-04) `bootstrap-linux.sh` | Versioned golden-image bootstrap for `td-ci-linux` — arrives with CI-04, not here. |
| (CI-04) `bootstrap-windows.ps1` | Versioned bootstrap for `td-ci-windows` — arrives with CI-04. |

## Bootstrap outline (what CI-04 must produce)

```text
td-ci-linux (desktop, Hyper-V)
  Ubuntu LTS · unprivileged `runner` account · no host mounts / clipboard / SSH agent / browser
  .NET 8 SDK · Node 24 · Docker + BuildKit (rootless where practical) · Playwright Chromium deps
  labels: self-hosted, taskdeck, trusted, linux, x64, heavy      (ci/policy.v1.json runnerClasses)
  one job at a time · NAT outbound only · caches bounded: NuGet, npm, Playwright browsers, BuildKit
  pre-job: clean workspace, remove untracked worktrees, clear temp     post-job: same + prune
  monthly: rebuild from the golden image; after any suspicious job: destroy the disk, rebuild

td-ci-windows (laptop or second VM)
  isolated Windows VM preferred; dedicated low-privilege local account as fallback
  labels: self-hosted, taskdeck, trusted, windows, x64, compatibility
  runs only the Windows compatibility contract (CI-07) on ordinary PRs; full sweep weekly/release
```

Rules the runners must satisfy (proven by CI-04's acceptance list, never assumed):

- ordinary self-hosted jobs receive **no** repository/environment/release secrets and a read-only
  token (`permissions: contents: read`, `persist-credentials: false`);
- T2/T3/R4 plans never resolve to a self-hosted class (`scripts/ci/smart-ci/plan.test.mjs`);
- `CI_EXECUTION_MODE=hosted` (repository variable) or the `ci:hosted` label forces every lane hosted;
- an offline runner leaves its job queued and the gate pending — never a false green;
- the incident path is: stop VM → remove runner → rotate anything exposed → discard disk → rebuild.
