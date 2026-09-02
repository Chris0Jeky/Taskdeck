# CI-10 — Nightly coordinator, weekly sweep, clean-from-tag release qualification (#2334)

Last Updated: 2026-09-02

> Curated from the v0.3/v0.4 acceleration bundle (grounded `221aa88c8`, 2026-08-30) and validated against `main` `de488fea0` on 2026-09-02 under tracker #2376 (follow-up to #2348). Planning input, not authority: the live issue, ADR-0066 §Decision 10, ADR-0052 and `docs/ci/SMART_CI.md` §6 win. Corrections to the bundle are in the last section.

## Outcome

One owner answers "what changed since the last deep qualification, and which checks would produce
new evidence tonight". A quiet night emits an honest green receipt instead of a full sweep; a
changed night runs the affected deep suites; once a week the full entropy sweep runs regardless;
and a release is qualified from the exact tag in a clean hosted context, never from promoted PR
artifacts.

## Live dependencies (verified 2026-09-02)

| Issue | State | Must deliver first | Unblocks |
| --- | --- | --- | --- |
| CI-01 `#2325` baseline | **closed** | `docs/ci/CI_BASELINE.md` — including the measured `CI Nightly` record: 31 runs, 24 success, **7 failures** (the mobile-safari lane `#2180`) | 01, 03 |
| CI-02 `#2326` planner | open (shadow scaffold merged) | `scripts/ci/smart-ci/plan.mjs` + `lib/plan.mjs` classify a change set into lanes. The coordinator's "diff classification" must reuse this, not fork it | 01 |
| CI-03 `#2327` gate + landed verifier | open | The **tree-SHA** receipt lookup on `push: main`. "Last successfully deep-qualified SHA" is the same class of marker and should share its storage decision | 01 |
| CI-12 `#2336` receipts | open | The nightly no-change receipt is a `ci-run` document; if CI-10 invents its own it forks the ledger CI-12 owns | 01, 03 |
| CI-09 `#2333` retention | open | Release/nightly artifacts are the bulk of the 372.1 GB overhang; retention classes must exist before the weekly sweep multiplies artifacts | 03, 04 |

Nothing on `main` references `#2334`. No coordinator, no deep-qualification marker and no weekly
schedule exists.

## Child slices (one PR each, in order)

| Id | Outcome | Depends on | Mode | Startable before predecessors merge? |
| --- | --- | --- | --- | --- |
| `CI10-1-coordinator` | A pure, testable coordinator module: given the last deep-qualified SHA, the `main` diff and the policy, return `{noChange \| affectedSuites[] \| weeklyFull}` plus the receipt fields | — | tooling (`scripts/ci/smart-ci/**` + `node --test`) | **Yes — start here.** It is a pure function over a change list; it can consume `lib/plan.mjs`'s `matchGroups()` today and be wired to a workflow later |
| `CI10-2-consolidate` | Merge `ci-nightly.yml` and `nightly-quality.yml` under the coordinator without dropping a single job; mutation stays `workflow_dispatch`-only | 01 | control-plane (R4/T2) | No — needs a proven selection function, and moving jobs before it exists loses coverage silently |
| `CI10-3-weekly` | The full entropy sweep: Linux + Windows + browsers + containers + dependency/SAST + performance, one fixed weekday | 02, CI-09 `#2333` | control-plane | No |
| `CI10-4-release` | Collapse the tag/release double trigger; qualify from the exact tag; verify digests and provenance | — (measurement) / 03 (for full evidence) | control-plane | **Partly.** Measuring whether the same commit is built twice is a read-only analysis startable now; changing the triggers is not |

## Architecture

| Concern | Type | Status | Note |
| --- | --- | --- | --- |
| Deep nightly today | `.github/workflows/ci-nightly.yml` — `schedule: '25 3 * * *'`, `workflow_dispatch` | **exists** | Backend solution, E2E smoke, cross-browser, k6, containers. No change detection: it runs every night unconditionally |
| Quality nightly today | `.github/workflows/nightly-quality.yml` — `schedule: '55 3 * * *'` | **exists** | 30 minutes after the other one, separate owner. This is the duplication the issue names |
| Mutation | `.github/workflows/mutation-testing.yml` | **exists, manual** | Its header records the schedule removal on 2026-08-19 under ARCHIVE-07 / `#1275` / ADR-0052. Confirmed: `on:` has only `workflow_dispatch`. **Do not re-add a cron** |
| Release triggers | `ci-release.yml` and `release-security.yml` | **exists — the double trigger is real** | Both declare `push: tags: v*` **and** `release: types: [published]`. Publishing a release for a pushed tag runs each workflow twice for one SHA |
| Release build | `ci-release.yml` jobs `release-build-verification`, `sbom-provenance`, `gitleaks`, `container-images` | **exists** | SBOM/provenance already ships via `reusable-sbom-provenance.yml` (OPS-11). Missing: an explicit exact-tag checkout ref and a digest-verification step |
| Digest verification helper | `scripts/ci/verify-sha256.sh`, `scripts/ci/validate-release-tag.sh` | **exists** | Reuse; do not write new hashing |
| Last-deep-qualified-SHA marker | — | **new** | The open decision. A repository variable, a tag, or a receipt query — see below |
| Weekly sweep | — | **new** | Nothing weekly exists in `.github/workflows/` today |

**The marker decision, framed.** ADR-0066 §Decision 3 already chose **tree SHA** as the identity
that binds a PR receipt to a landed commit. Reusing tree SHA for deep qualification keeps one
identity concept in the fabric and survives rebases that do not change content. A repository
variable is mutable by anyone with write access and carries no history; a lightweight tag pollutes
the tag namespace that `ci-release.yml` triggers on (`v*` is filtered, but a reader is not). Prefer:
the last successful deep run's receipt, looked up through the Actions API by workflow + conclusion,
with the tree SHA recorded in it — no new mutable state.

**No-change semantics.** "Honest green" means a receipt that says *no new evidence was required*
with the compared SHAs in it — not a skipped workflow. A skipped job reports as successful to
branch protection, which is exactly the failure mode `docs/ci/SMART_CI.md` invariant 1 forbids.

## Implementation plan

**Preflight.** Measure the duplicate release qualification before changing anything: list
`ci-release.yml` runs and group by head SHA; if the same SHA has a `push` run and a `release` run,
that is the measured duplication the issue asks you to collapse. Record the number in the PR.

**Producer-owned paths:** `scripts/ci/smart-ci/nightly-coordinator.mjs` + `*.test.mjs`.

**Integration-owner seams** (R4/T2, hosted-only, one CI control-plane owner):
`.github/workflows/ci-nightly.yml`, `nightly-quality.yml`, `ci-release.yml`, `release-security.yml`,
`mutation-testing.yml` (do not touch its trigger), `ci/policy.v1.json`, `docs/ci/SMART_CI.md` §6,
`docs/STATUS.md` §CI Status (line 729).

**Rollout / rollback.** Land the coordinator computing its verdict and **logging it** while the
existing nightlies still run unconditionally, for at least one week including one weekend. Only
after a real quiet night proves the verdict correct does the coordinator gate execution. Rollback is
reverting the `if:` on the deep jobs — the coordinator module itself changes no behaviour.

**Definition of done.** `docs/STATUS.md` §CI Status names one nightly owner. ADR-0052's mutation
verdict is restated, not amended. `#1210` (ci-extended `startup_failure`, Testcontainers lane) and
`#2180` (mobile-safari red since 2026-08-24) are each fixed, retired with a recorded reason, or
carried as a CI-15 `#2339` quarantine entry with an owner and expiry — the issue makes them this
lane's ownership, so closing CI-10 without dispositioning them is not allowed.

## Test plan

- [ ] Coordinator: no relevant change since the last deep SHA → `noChange` verdict and a receipt naming both SHAs — `node --test scripts/ci/smart-ci/nightly-coordinator.test.mjs`
- [ ] Coordinator: a backend-only change selects backend deep suites and **not** the browser matrix
- [ ] Coordinator: the weekly slot forces `weeklyFull` even when the verdict would be `noChange`
- [ ] Coordinator: the last deep receipt is missing or unreadable → **full sweep** (fail-closed, matching planner invariant 2), never `noChange`
- [ ] Coordinator: a force-moved tag / rewritten `main` history makes the last SHA unreachable → full sweep, with the reason in the receipt
- [ ] Coordinator: two runs of the coordinator for the same SHA are detected as duplicate qualification (feeds the CI-12 duplicate flag)
- [ ] Release: the same head SHA reached by `push: tags` and by `release: published` produces exactly one qualification run (assert on the collapsed trigger, not on a comment)
- [ ] Release: the build job checks out the exact tag ref and the produced artifact digest matches the recorded provenance — reuse `scripts/ci/verify-sha256.sh`
- [ ] Release: no step consumes a cache key or artifact produced by a `pull_request` run (grep the workflow diff for `actions/cache` restore-only keys and `download-artifact` across workflows)
- [ ] Workflows: `node --test scripts/ci/*.test.mjs` and the release-workflow contract check stay green
- [ ] Docs: `node scripts/check-docs-governance.mjs`

## Edge cases

- Force-moved `v*` tag: the previously qualified digest no longer matches the tag — qualification must fail loudly, not rebuild silently.
- `release: published` fires again when a draft release is edited and re-published — idempotency must be by SHA, not by event.
- The last-qualified receipt has expired from artifact retention (14 days today, and CI-09 will shorten it) → treat as missing → full sweep.
- A nightly partially fails: some deep suites green, some red. The "last deep-qualified SHA" must only advance on a *complete* success.
- The weekly sweep collides with a release qualification on the same runner budget — the weekly slot must be a fixed weekday chosen away from release cadence.
- A quiet night immediately after a force-push to `main`: the diff base is gone; fail closed.
- Clock/timezone: `'25 3 * * *'` is UTC; a "weekly on day N" rule must be computed in UTC or it drifts across DST for a human reader.

## Reference material

| Kind | Archived path | Good for | Caveats |
| --- | --- | --- | --- |
| Blueprint | `docs/analysis/2026-08-30-acceleration-bundle/architecture/SMART_CI_DEPTH_BLUEPRINT.md` §Nightly/release | The five-line coordinator contract and the release clean-room list | See its validation preface |
| Diagram | `.../diagrams/smart-ci-control-loop.svg` | Where the nightly coordinator sits relative to the PR gate | Explanatory only |
| Issue pack | Bundle `01_MILESTONE_5/issue-packs/2334.md` | The CI10-1..4 slice names, kept above | Its "depends on #2324" and its `.github/workflows/**` ownership list are correct; its decision list is partly already-decided (correction 2) |
| Live evidence | `docs/ci/CI_BASELINE.md` | `CI Nightly`: 31 runs / 7 failures in the window; `CI` required: **341 `push` to main runs** in 31 days, all duplicating a PR run | The push-to-main duplication is CI-03's phase-3 item, not CI-10's — do not fold it in |

## Corrections to the bundle

1. **Bundle:** "Nightly/release responsibilities are split and can duplicate exact-SHA
   qualification." **True on `main`, and stronger than stated:** both `ci-release.yml` and
   `release-security.yml` carry `push: tags: v*` **and** `release: types: [published]`, so the
   duplication is two workflows x two triggers, not one. **Consequence:** slice 04 must collapse
   both files, and `release-security.yml` must be named in the issue's scope, which currently omits it.
2. **Bundle:** lists "Canonical deep-qualified SHA marker" and "Tag versus release event trigger
   owner" as decisions to receive. **True:** ADR-0066 §Decision 3 already fixed **tree SHA** as the
   fabric's commit-identity concept for receipt binding. **Consequence:** the marker is a
   consistency choice, not an open product decision; record it in the issue and move on. The
   trigger-owner question is real and stays open.
3. **Bundle:** "preserve manual mutation ruling". **True and verified:** `mutation-testing.yml`'s
   header records the removal under ADR-0052 / `#1275` on 2026-08-19 and `on:` contains only
   `workflow_dispatch`. **Consequence:** correct as written — but note ADR-0066's relationship table
   marks ADR-0052 **Retained** for exactly this verdict, so re-adding a schedule needs an ADR
   amendment, not a workflow edit.
4. **Bundle:** file ownership names `docs/STATUS.md` generically. **True:** the section is
   `docs/STATUS.md` §CI Status at line 729, and the issue's own acceptance box requires it.
5. **Bundle:** "SBOM/provenance/digest and protected signing" is listed as new CI10-4 work.
   **True:** `ci-release.yml` already calls `reusable-sbom-provenance.yml` (job `sbom-provenance`,
   OPS-11 / `#103`) and `scripts/ci/verify-sha256.sh` exists. **Consequence:** the genuinely new
   parts are the exact-tag checkout ref, the digest-vs-provenance assertion, and the no-PR-artifact
   guarantee — not SBOM generation.
6. **Bundle:** presents the nightly consolidation as safe to do first. **True:** `docs/ci/SMART_CI.md`
   §9 places nightly consolidation in **phase 3 (dedupe)**, gated on recall staying 100% and a
   weekly sweep existing. **Consequence:** slice 02 cannot merge before slice 03 has a weekly sweep
   to consolidate *into*; the bundle's 1→2→3→4 order inverts that dependency.
7. **Bundle:** silent about `#1210` / `#2180`. **Live issue scope** makes both this lane's
   ownership, and `docs/ci/CI_BASELINE.md` measures 7 nightly failures in the window from `#2180`.
   **Consequence:** they are acceptance-relevant, not background.
