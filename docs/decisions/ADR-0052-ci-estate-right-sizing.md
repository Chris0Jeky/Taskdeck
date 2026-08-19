# ADR-0052: CI Estate Right-Sizing — Keep/Fix/Kill/Gate Verdict Per Scheduled Lane

- Status: Accepted
- Date: 2026-08-19
- Deciders: Repository maintainers (2026-08-19 ruling: adopt the `#1275` acceptance criteria as written, amended for the parallel v0.1.0 ship)
- Related: ARCHIVE-07 (`#1275`), `#1358`/PR `#1359` (k6 repair), `#1449`/`#1445` (k6 threshold recalibration), `#1500` (mutation lane repair), `#1173` (branch protection), ADR-0013 (CI topology), ADR-0035 (required security-scan gate)

## Context

The 2026-07-02 whole-project analysis (`docs/COURSE_CORRECTION.md`) found the required PR gate
healthy but the *scheduled* CI estate around it oversized and partly dead: `ci-nightly` red for 28
consecutive days on two k6 jobs, `mutation-testing` red 10+ weeks on a Stryker schema error while
STATUS described it as delivered, `ci-release`/`release-security` producing SBOM/SLSA distribution
artifacts for a then-never-distributed project, and `pages-frontend` reported failing on a deploy
timeout with no recorded keep/disable decision.

Between the issue's authoring and this decision, two of those lanes were repaired on their own
tracks: `#1358`/PR `#1359` fixed the k6 summary-export ownership bug and added a fail-closed
capacity gate, `#1449`/`#1445` recalibrated the k6 tail thresholds, and `#1500` repaired the
mutation lane's Stryker config. This ADR reconciles the issue's keep/fix/kill/gate criteria against
the **live** 2026-08-19 state, records the per-lane verdict, and closes the archive-era requirement
that the repo document its own protection posture.

The maintainer amended one criterion: because v0.1.0 is being tagged in parallel this session, the
release lanes are **gated to release tags** rather than reduced to `workflow_dispatch`-only.

## Decision

Right-size the scheduled estate to a per-lane verdict. End state: zero workflows that fail on every
run; every remaining workflow is green or its schedule is removed with a dated comment.

| Lane | Verdict | Action |
|------|---------|--------|
| `ci-required.yml` | KEEP | Unchanged. The required PR gate. |
| `release-desktop.yml` | KEEP | Unchanged. |
| `ci-nightly.yml` (k6 jobs) | KEEP (already fixed) | No change. Green on the last 8 consecutive nightlies (2026-08-11..08-18). The permission bug and thresholds were already repaired by `#1358`/`#1359` and `#1449`. |
| `mutation-testing.yml` | KILL schedule | Weekly `cron` removed; `workflow_dispatch` retained. Header + `on:` block carry a dated comment. |
| `ci-release.yml` | KEEP (gated) | Already `push: tags: v*` + `release: published` + `workflow_dispatch`, no schedule. Correct for the v0.1.0 tag ship. |
| `release-security.yml` | KEEP (gated) | Same trigger set as `ci-release`; no schedule. |
| `pages-frontend.yml` | KEEP | Green; the stale deploy-timeout report is superseded by live evidence. |

### k6 board-write regression triage (evidence, not speculation)

The board-write p95 signal is **not a code regression**. `tests/load/k6/board-heavy-load.js`
documents the measured shape: median ≈ 12 ms with a heavy-tailed p95 ≈ 2.0-3.0 s at 20 VUs — the
single-writer SQLite write-convoy capacity ceiling, an architectural property, not a drift. The
lane now gates board-write at a `p(95)<4500` tail (1.5× the measured 2000 ms ceiling) and keeps the
always-on aggregate `http_req_duration: p(95)<2000` as an informational near-capacity warning
(`#872`). The two k6 jobs (Load and Concurrency Harness, Performance Regression Gate) have passed on
every nightly for 8 straight nights, so there is no live regression to chase; the 2000 ms ceiling is
recorded as **documented capacity**, per the `#1275` 2026-07-10 re-scope comment.

### Branch protection state (`#1173`, recorded per AC)

`main` runs **classic** branch protection (no ruleset). Required status checks: `strict: false`,
contexts limited to the three security scans — `Dependency Security / Dependency Security Signals`,
`SAST Scan / SAST Scan (Semgrep)`, `Secret Scan / Gitleaks Scan`. `required_approving_review_count:
0`, `require_code_owner_reviews: false`, `enforce_admins: false`, `allow_force_pushes: false`,
`allow_deletions: false`, `required_signatures: false` (DCO is a separate check app). Notably
`ci-required` is **not** itself a required context; it gates by convention and the `review-and-ship`
pipeline, not by branch-protection enforcement.

## Alternatives Considered

- **Reduce `ci-release`/`release-security` to `workflow_dispatch`-only (the issue AC as written).**
  Rejected by the maintainer amendment: v0.1.0 is being tagged now, so tag-triggered SBOM/provenance
  is wanted. The current tag/release/dispatch triggers already exclude any schedule, which was the
  actual defect.
- **Delete the mutation lane entirely.** Rejected: the `#1500` repair is sound and the score is a
  useful on-demand calibration signal. Removing only the weekly `cron` keeps the capability without
  spending a 180-minute run every week for a non-gating number.
- **Disable `pages-frontend`.** Rejected: live evidence shows it green in ~40 s per deploy; the
  2026-07 timeout report is stale.
- **Re-open / re-tune the k6 thresholds.** Rejected: they were already recalibrated (`#1449`) and
  have held green for 8 nights; re-tuning now would be churn without a failing signal to justify it.

## Consequences

- **Positive.** No scheduled lane fails on every run. The weekly mutation run stops consuming a
  180-minute slot for a non-blocking metric. The per-lane verdict and the SQLite capacity ceiling are
  documented where a future contributor will find them. Branch-protection posture is recorded in-repo.
- **Negative / neutral.** Mutation score is no longer captured automatically; it must be triggered on
  demand (`workflow_dispatch`), so drift between runs is invisible unless someone dispatches it. The
  board-write capacity ceiling remains a real limit of the single-writer SQLite design — documented,
  not removed.
- **Not changed.** No required-gate, application, or release-artifact behavior changes; the only
  functional workflow edit is removing the mutation `cron`. Workflow trigger/schedule changes cannot
  be fully proven locally — they are exercised only by GitHub Actions on merge.

## References

- Issue: `#1275` (ARCHIVE-07), re-scope comments 2026-07-06 / 2026-07-10 / 2026-07-14; related `#1210`, `#1228`, `#1173`.
- Repairs relied on: `#1358`/PR `#1359`, `#1449`/`#1445`, `#1500`.
- Live evidence (2026-08-19): `gh run list --workflow=ci-nightly.yml` (8 consecutive successes), `gh run view 32097652687` (both k6 jobs green), `gh run list --workflow=pages-frontend.yml` (green), `gh api repos/Chris0Jeky/Taskdeck/branches/main/protection`.
- Thresholds: `tests/load/k6/board-heavy-load.js` lines 22-40.
