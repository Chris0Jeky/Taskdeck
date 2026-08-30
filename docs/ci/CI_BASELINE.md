# CI baseline — 2026-07-31 .. 2026-08-30 (CI-01 `#2325`)

Last Updated: 2026-08-30 · Tool: `scripts/ci/smart-ci/measure-ci-estate.mjs` · Ledger: [`baselines/ci-estate-2026-08-30.md`](baselines/ci-estate-2026-08-30.md) (+ `.json`) · Decision: ADR-0066

This is the measured starting point for the Smart CI Fabric. It replaces the pack's one-day snapshot
(`docs/analysis/2026-08-30-smart-ci/CURRENT_CI_MEASUREMENTS_AS_RECEIVED.json`; PR #2280 timings are
kept there as a historical sample only). Re-run the tool after every topology change and **append** a
new dated ledger — never overwrite this window.

## Method

- 31 UTC days, listed one day at a time (the Actions runs API returns at most 1,000 results per
  filter; a single-window call returned only 2026-08-22..08-30). No day reached the cap.
- Job duration = `completed_at − started_at` (queue time excluded); critical path = last job
  completion − first job start within one run.
- **Allowance minutes** apply private-repository accounting to measured durations: every job rounds
  up to a whole minute; Windows counts ×2, macOS ×10 against the GitHub Pro allowance of 3,000
  minutes/month; storage allowance 1 GB (Actions + Packages), billed per GB-day beyond that. The
  public repository reports 0 billable minutes today.
- The sample is the 30 most recent **green** `CI` (`ci-required.yml`) runs on `pull_request`; failed
  and cancelled runs are counted but not sampled.
- Prices, per-job rounding and the Pro allowances were verified on docs.github.com on 2026-08-30:
  Linux 2-core $0.006/min, Windows 2-core $0.010/min, macOS $0.062/min, Linux 1-core $0.002/min;
  "GitHub rounds the minutes and partial minutes each job uses up to the nearest whole minute";
  Pro private repositories include 3,000 minutes/month and 1 GB shared storage; shared storage beyond
  that is $0.25 per GB-month, Actions cache $0.07 per GB-month; "Each Copilot code review consumes
  GitHub Actions minutes in addition to AI credits" (private repositories draw on the plan
  entitlement). **Assumption, not re-verified on those pages:** the Windows ×2 / macOS ×10 allowance
  multipliers (GitHub's long-standing rule). If it no longer applies, Windows allowance consumption
  halves (≈82 instead of ≈126 allowance minutes per run) — every ranking below still holds because
  Windows is also the slowest platform and the highest per-minute price. Re-read at cutover (CI-13 A).

## Headline numbers

| Measure | Value |
| --- | --- |
| Workflow runs in the window | **2,721** (cancelled 504 = 18.5%; 58 runs re-run, 62 extra attempts) |
| `CI` (required) runs | **1,205** — 864 `pull_request` + **341 `push` to main**; 735 success, 79 failure, **386 cancelled (32%)**, 55 re-run |
| `CI Extended` runs | 816 (708 success, 106 cancelled) |
| `Deploy Frontend to GitHub Pages` | 139 pushes |
| `CI Nightly` | 31 (24 success, **7 failure** — the mobile-safari lane, `#2180`) |
| Green required run: jobs | 17 |
| Green required run: critical path | **p50 24.7 min**, p95 37.9, max 40.5 |
| Green required run: aggregate runner minutes | p50 71.7, mean 72.4 (means: Linux 30.7 + Windows 41.8) |
| Green required run: **allowance minutes** | **p50 125**, mean 126.1 (means: Linux 38.2 + Windows 87.9), p95 133 |
| Projection, current topology, completed runs | 788 runs/month × 126.1 = **~99,000 allowance min/month** (99,367) vs 3,000 included |
| Projection, `pull_request`-only (no main re-run) | 583 completed `pull_request` runs/month × 126.1 = ~73,500 |
| Projection, everything incl. cancelled (upper bound) | ~147,000 |
| Actions cache | **9.76 GiB (10.48 GB decimal) / 183 caches** — at the 10 GiB cap (it read exactly 10.0 GiB two hours earlier; eviction in action) |
| Artifacts on record | 32,051 (13,717 expired) |
| **Unexpired artifact storage** | **372.1 GB** (18,334 artifacts; oldest 2026-06-02) vs 1 GB allowance |
| ↳ `container-image-artifacts` | 2,067 artifacts = **358.7 GB** (exported image tars, default 90-day retention) |
| ↳ `frontend-unit-artifacts` | 3,728 = 8.1 GB |
| ↳ `performance-regression-gate-results` + `load-harness-k6-results` | 1.7 + 1.3 GB |

Per-job means over the sample (Windows jobs in bold carry 70% of the allowance cost):

| Job | OS | mean | allowance min |
| --- | --- | ---: | ---: |
| **API Integration (windows-latest)** | windows | 17.3 min | 35.7 |
| **Frontend Unit (windows-latest)** | windows | 12.2 min | 25.4 |
| API Integration (ubuntu-latest) | linux | 7.2 min | 7.7 |
| E2E Smoke | linux | 7.0 min | 7.6 |
| **Backend Unit (windows-latest)** | windows | 6.2 min | 13.7 |
| **Docs Governance / Worktree Helper (Windows PowerShell)** | windows | 6.2 min | 13.4 |
| Backend Unit (ubuntu-latest) | linux | 5.1 min | 5.6 |
| Frontend Unit (ubuntu-latest) | linux | 4.1 min | 4.7 |
| Container Images | linux | 2.2 min | 3.0 |
| Migration Validation | linux | 1.7 min | 2.0 |
| SAST, Dependency Security, Architecture, Docs Governance, Release Workflow Contract, Gitleaks, Paper Color Audit | linux | ≤1 min each | 1 each (rounding) |

## Findings

1. **The current topology cannot run on the Pro allowance.** ~99,000 allowance minutes/month is 33× the
   3,000 included; at the verified prices a completed required run costs ≈ $0.67 beyond the allowance
   (38 Linux min × $0.006 + 44 Windows min × $0.010), i.e. **≈ $500/month at the August run rate**
   before nightly, extended and release lanes. Dropping the `push: main` re-run alone removes 42%
   of the *completed* required runs (341 of 814; 341 of all 1,205 runs including cancelled) — ADR-0066 §3, CI-03 `#2327`.
2. **Windows is the cost centre, not the semantics centre.** Windows is 42 of 72 runner minutes but
   88 of 126 allowance minutes per run (70%); the API suite is 2.4× slower on Windows (17.3 vs 7.2 min)
   and the frontend leg 3× (12.2 vs 4.1) — the Linux baseline + Windows compatibility contract
   (CI-07 `#2331`) and the harness repair (CI-06 `#2330`) are where the minutes are.
3. **One third of required runs are cancelled** (386 of 1,205): superseded by the next push under
   `cancel-in-progress`. Each still consumed minutes until cancellation. Draft-mode light plans and
   local preflight (CI-03 topology) are the fix; cancellation is not an economy measure.
4. **Storage is 370× the allowance before a single minute is spent.** 359 GB of exported container
   image tars from `reusable-container-images.yml` (no `retention-days`, so the 90-day default;
   nothing consumes them) plus 8 GB of frontend artifacts. At GitHub's published storage rate
   (372 GB × $0.25 per GB-month ≈ **$93/month**) it is
   smaller than the minutes but billed from day one of being private and the cheapest thing to fix:
   retention classes and a one-time maintainer-authorized cleanup (CI-09 `#2333`,
   `OUTSTANDING_TASKS.md` §J SC-2).
5. **The cache is at its cap** (9.76 GiB / 183 caches after evicting from exactly 10.0 GiB earlier the same day) and therefore already evicting — cache keys
   need owners and bounds (CI-09).
6. **Duplicate qualification is by event, not by SHA.** Zero head SHAs were qualified twice (merge
   commits get new SHAs), yet 341 `push` runs re-ran the full suite on trees the PR had just
   qualified — the tree-SHA landed verifier (ADR-0066 §3) is the correct binding.
7. **Nightly is red 7 of 31 nights** (`#2180`) and `CI Extended` fired on 815 `pull_request` runs against the
   required lane's 864 (its path filter covers almost every change) — deep ownership consolidates under CI-10 `#2334`.
8. **The pack's PR #2280 sample was representative**: its Windows API 15.5 min / Ubuntu 6.0 min
   test-step figures sit inside this window's job-level distribution (17.3 / 7.2 min job means).

## What this baseline does not say

- It does not measure minutes of the non-required lanes (`CI Extended`, nightly, release) per run;
  their run counts are in the ledger and their per-job costs follow in the CI-12 receipts.
- Artifact families are grouped by name prefix (OS/run suffixes stripped) — a heuristic for ranking,
  not an accounting unit.
- The listing exposes each run's latest attempt only: 62 extra attempts consumed minutes this ledger
  does not see, and the sampled jobs are those of the latest attempt.
- The ledger was regenerated once on 2026-08-30 after the tool's per-event projection and
  workflow-grouping fixes (review of PR #2341); the committed figures are the regenerated run.
- Prices and allowances are as cited on 2026-08-30 and must be re-read before the cutover.

## Re-measure

```text
node scripts/ci/smart-ci/measure-ci-estate.mjs --since <YYYY-MM-DD> --until <YYYY-MM-DD> --sample 30 --out-dir docs/ci/baselines
```

Read-only; needs `gh auth token` or `GH_TOKEN`; ~4 minutes (the artifact listing is ~320 pages).
