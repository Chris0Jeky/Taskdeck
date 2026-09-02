# REF — v0.4 refactoring pass: measurement first, one seam per PR (#2236)

Last Updated: 2026-09-02

> Curated from the v0.3/v0.4 acceleration bundle (grounded `221aa88c8`, 2026-08-30) and validated against `main` `de488fea0` on 2026-09-02 under tracker #2348 follow-up. Planning input, not authority: the live issue and its three comments (the newest is a **park receipt** for PR #2356), `docs/STATUS.md` and the architecture tests win. Corrections to the bundle are in the last section.

## Outcome

Pick refactor targets from checked-in evidence rather than intuition, then move exactly one boundary
per PR with observable behaviour held constant. The score is explainable and reproducible; the
authoritative ranking is taken at the `v0.3.0` tag, not before.

## Live dependencies (verified 2026-09-02)

| Item | State | Relationship |
| --- | --- | --- |
| PR **#2356** `issue-2236/refactor-measurement-tooling` | **OPEN, ready, PARKED** | 3 files, +747/-0: `scripts/analysis/rank_refactor_candidates.py`, `scripts/analysis/test_rank_refactor_candidates.py`, `docs/analysis/refactoring/README.md`. Head `c3c04a26b`. Parked at the review-loop ceiling on a confirmed HIGH (see below). None of these paths exist on `main` |
| `v0.3.0` tag | not yet cut (`v0.3.0-rc.1` published 2026-08-30) | REF-1's baseline waits for the exact final tag; REF-0 does not |
| `#2007`, `#1968` keyboard-shortcut ledger | open | Named candidates; `frontend/taskdeck-web/src/utils/keyboardShortcuts.ts` exists (started by PR #2226) |
| `#2141` two skins | open | Legacy-vs-Paper retirement boundary is a **product decision**, not a measurement output |
| `#1770` i18n catalogs | open | Named candidate; also the reason the eager-bundle budget moved to 1250 KB |
| Context Fabric wave (`#2256`–`#2277`) | open | The reason `CaptureService` must not be refactored now — the issue's own second comment says so |

## Child slices (one PR each, in order)

| Id | Outcome | Depends on | Mode | Startable before predecessors merge? |
| --- | --- | --- | --- | --- |
| `REF-0` measurement tooling | The ranker + fixtures + exclusion contract, no production behaviour change | — | tooling | **In flight and parked.** Do not open a second PR — restart #2356 against its recorded restart contract (below) |
| `REF-1` baseline | Run at the exact `v0.3.0` tag; record base/head SHAs, dirty state, formula, exclusions; inspect the top 20 manually; nominate five seams | REF-0 merged **and** the tag cut | measurement | No — two hard predecessors |
| `REF-2..6` seams | One issue and one PR per selected seam; characterization tests first | REF-1 | implementation | No — selecting a seam before the ranking exists is the thing this issue exists to prevent |
| `REF-7` guardrails | Size/architecture guards derived from the committed evidence | REF-2..6 | tooling | No |

**REF-0's restart contract** (from the issue's 2026-08-31 comment, verbatim in substance):
make rename lineage aggregation independent of traversal order — build final-path equivalence
*before* aggregating totals; add a synthetic sibling-branch edit + rename regression and prove the
result is stable when incomparable commit dates or order change; retain all 16 current regressions
and the exact object-policy guarantees; preserve a trailing-space repository root instead of
`.strip()`-ing it; skip and count irrelevant non-UTF-8 paths rather than aborting every candidate;
rerun the two byte-stable real reports, the exact-base review, and the hosted gates.

## Architecture

| Concern | Type | Status | Note |
| --- | --- | --- | --- |
| Score | `ln(1 + lines) x ln(1 + churn) x sqrt(max(1, touching_commits))` | **agreed** | Adopted in the issue's first comment and in RECONCILIATION.md; the bundle and PR #2356 agree on it |
| Ranker | `scripts/analysis/rank_refactor_candidates.py` | **not on `main`** — lives only in PR #2356 | `ls scripts/analysis` on `main`: no such directory |
| Layer boundaries | `backend/tests/Taskdeck.Architecture.Tests` | **exists** | The guard every refactor PR must keep green; REF-7 extends it rather than adding a parallel linter |
| Frontend guards | `frontend/taskdeck-web` guard specs + `scripts/ci/check-bundle-size.mjs` (eager-graph budget **1250 KB**, history 1200 → 1250 on 2026-08-19 for `#1770`, a 1280 stopgap on 2026-08-22) | **exists** | A refactor that moves imports can move the eager graph; this budget is the tripwire |
| Shortcut registry | `frontend/taskdeck-web/src/utils/keyboardShortcuts.ts` | **exists (partial)** | Started by PR #2226; the three-system consolidation is unfinished |
| Largest seams (measured `wc -l`, 2026-09-02) | see below | **exists** | The issue's second comment ranked by *code* LOC from a clean `git archive`; raw line counts today are consistent with it and the ordering has shifted |

Raw `wc -l` on `main` `de488fea0` (raw lines, not code LOC — the comment's numbers were code LOC):

| File | Raw lines | Comment's code LOC (2026-08-30) |
| --- | ---: | ---: |
| `frontend/taskdeck-web/src/views/paper/PaperReviewView.vue` | 2,274 | ~1,600 |
| `backend/src/Taskdeck.Application/Services/AutomationProposalService.cs` | 2,221 | ~1,574 |
| `backend/src/Taskdeck.Api/FirstRun/FirstRunBootstrapper.cs` | 1,620 | ~1,227 |
| `backend/src/Taskdeck.Application/Services/CaptureService.cs` | 1,396 | ~1,061 |
| `frontend/taskdeck-web/src/views/paper/PaperBoardView.vue` | 1,289 | ~1,020 |
| `backend/src/Taskdeck.Application/Services/DataExportService.cs` | 1,220 | ~1,013 |
| `backend/src/Taskdeck.Application/Services/OpenAiCompatibleLlmProvider.cs` | 1,178 | ~1,047 |
| `backend/src/Taskdeck.Api/Program.cs` | 677 | — |
| `backend/src/Taskdeck.Api/FirstRun/DesktopRuntime.cs` | 322 | — |

**Ranking is not selection.** The score is a *shortlist generator*; the issue's own method inspects
the top 20 by hand for coupling and responsibility count. Two of the top seams are explicitly
excluded by the issue's second comment: `CaptureService` and `DataExportService` are enlarged by the
in-flight Context Fabric work and must be re-measured at selection time, not refactored now.

## Implementation plan

**Preflight.** Read PR #2356's park receipt on the issue before writing a line. The blocker is
specific and reproducible: if one branch edits `old.cs` while a sibling branch renames it to
`new.cs`, `rev-list` can visit the old-path edit *before* the rename; `collect_churn()` then records
`old → new` but never migrates the already-accumulated churn and touch sets, so the current path is
undercounted — the reproduced run reported `new.cs` as churn 0 / one touch instead of churn 1 / two
touches.

**Producer-owned paths:** `scripts/analysis/**`, its focused tests, `docs/analysis/refactoring/**`.
Nothing else. No production file, no formatting pass, no rename mixed with behaviour.

**Rollout / rollback.** REF-0 changes no product behaviour, so rollback is deletion. REF-2..6 each
ship behind characterization tests written *first* and merged in the same PR; the rollback for a
seam PR is a revert, which stays clean only because the PR touches one seam.

**Definition of done.** For REF-0: the ranker is byte-stable across two runs on the same clean
checkout, the exclusion contract is checked in (not implicit), and dirty-tree behaviour is explicit
rather than best-effort. For each REF-2..6: `dotnet test backend/tests/Taskdeck.Architecture.Tests/Taskdeck.Architecture.Tests.csproj -c Release -m:1`
green, the frontend guard specs green, the eager-bundle budget unchanged, and an ADR whenever a
boundary actually moves.

## Test plan

- [ ] Ranker: score function unit cases (zero lines, zero churn, one commit, large values) — `py -3 -B -m unittest discover -s scripts/analysis -p "test_*.py"`
- [ ] Ranker: **rename lineage** — a sibling-branch edit to `old.cs` plus a rename to `new.cs`, with the edit visited first, aggregates onto `new.cs`; the result is identical when commit dates are made incomparable or the traversal order is reversed (the #2356 blocker)
- [ ] Ranker: merge commits are excluded so churn is not double-counted
- [ ] Ranker: a dirty worktree either fails closed or is reported in the output header — never silently measured
- [ ] Ranker: an unresolvable base ref fails closed with the ref named
- [ ] Ranker: a non-UTF-8 tracked path is skipped and **counted**, not silently zeroed and not fatal to the whole run
- [ ] Ranker: the exclusion contract is data, and a path that merely *contains* an excluded segment (`.../robin/x.cs` versus `bin/`) is **not** excluded (correction 3)
- [ ] Ranker: two runs on the same clean checkout produce byte-identical output
- [ ] Seam PRs: characterization tests exist and pass before the structural change in the same PR
- [ ] Seam PRs: `dotnet test backend/tests/Taskdeck.Architecture.Tests/Taskdeck.Architecture.Tests.csproj -c Release -m:1`
- [ ] Seam PRs (frontend): `cd frontend/taskdeck-web; npm run typecheck; npm run build; npx vitest --run --maxWorkers=2` and the eager-bundle budget unchanged
- [ ] Docs: `node scripts/check-docs-governance.mjs`

## Edge cases

- A large, stable file (a generated-looking catalog, a long switch) ranks high on size alone with near-zero churn — the manual top-20 review exists for exactly this.
- A tiny, very-high-churn generated file (an i18n catalog, an EF snapshot) — must be in the exclusion contract, and `Migrations/` already is.
- Renames that split history, including a rename *and* an edit in the same commit.
- Binary and non-UTF-8 paths: `git log --numstat` prints `-`/`-` for binaries; that is a skip, not a zero.
- Path reuse: a file deleted and a different file later created at the same path merges two unrelated histories.
- Git attributes (`-diff`, `text=auto`) change numstat output for some paths.
- A path with a literal backslash or a trailing space in the repository root — both are in #2356's residual list.
- Measuring the working tree rather than the exact commit tree: `.worktrees/` holds ~70 preserved Codex checkouts, which is why the issue's second comment re-measured from a clean `git archive`.

## Reference material

| Kind | Archived path | Good for | Caveats |
| --- | --- | --- | --- |
| Python candidate | `docs/analysis/2026-08-30-acceleration-bundle/candidates/python/rank_refactor_candidates.py` (145 lines) | The formula, the CSV/JSON output shape and a starting exclusion list | The un-hardened ancestor of PR #2356. No `--no-merges`; **no rename handling at all**; `line_count()` returns `0` on any decode/OS error; substring exclusions over-match; reads the working tree, not the commit tree. See corrections 2–4 |
| Candidate tests | `.../candidates/python-tests/test_rank_refactor_candidates.py` | Runs today from the archive (see `candidates/python-tests/_load.py`) | Covers the score function only — not the Git-lineage behaviour where every real defect lives |
| Blueprint | `.../architecture/PERFORMANCE_AND_REFACTOR_METHOD.md` §Refactor ranking | The exclusion categories (generated, vendor, lock, migration snapshot, binary) and "inspect the top 20 manually" | See its validation preface |
| Diagram | `.../diagrams/milestone-5-dependency-graph.svg` | Milestone-5 shape | Advisory only — RECONCILIATION.md records that the graph's JSON repeats ordered pairs as both `depends-on` and `unblocks` without documenting direction |

## Corrections to the bundle

1. **Bundle:** "REF-0 measurement tooling: Check in `rank_refactor_candidates.py`" as if it were
   unstarted. **True:** PR **#2356** exists, is ready-for-review, and is **PARKED** at the two-round
   ceiling on a confirmed HIGH (order-dependent rename attribution), after three earlier blocking
   cycles for merge double-counting, mutable-worktree/exact-SHA handling, and replacement-object /
   graft provenance. **Consequence:** the head-start here is the restart contract above, not a new
   branch. Opening a second REF-0 PR would fork a parked review.
2. **Bundle candidate:** `collect_churn()` runs `git log --numstat` with **no `--no-merges`**.
   **True:** merge commits repeat their children's numstat, so churn double-counts on every merged
   branch — a repository whose whole history is merge commits (`--merge` only, never squash) would
   be badly skewed. **Consequence:** already fixed in #2356; never reintroduce.
3. **Bundle candidate:** `is_candidate()` excludes when `any(part in normalized for part in excludes)`
   — a plain substring test. **True:** `'bin/'` therefore excludes any path containing `bin/`
   anywhere, e.g. `src/robin/x.cs`. **Consequence:** silent under-coverage of the ranking; the
   exclusion contract must be anchored path segments or globs.
4. **Bundle candidate:** `line_count()` returns **0** on `UnicodeDecodeError`/`OSError`.
   **True:** an unreadable file silently scores 0 and disappears from the ranking with no signal —
   a swallowed error, which the repository's definition of done forbids. **Consequence:** #2356's
   "skip and count" residual is the right shape; make it explicit rather than implicit.
5. **Bundle candidate:** reads line counts from the **working tree** (`line_count(path)`).
   **True:** this repository keeps `~70` stale worktrees under `.worktrees/` and the issue's second
   comment had to re-measure from a clean `git archive` to avoid double-counting.
   **Consequence:** measure the exact commit tree, and make dirty state a first-class, reported fact.
6. **Bundle:** "Views over 300 lines" and "`FirstRunBootstrapper.cs` (~700 lines)" from the issue's
   own candidate list. **True on `main`:** `FirstRunBootstrapper.cs` is at
   `backend/src/Taskdeck.Api/FirstRun/FirstRunBootstrapper.cs` and is **1,620 raw lines**, more than
   double the issue's figure; `DesktopRuntime.cs` is 322 and `Program.cs` 677 — both far smaller than
   the bootstrapper. **Consequence:** correct the issue's pointer and its size; the startup seam's
   weight is almost entirely in the bootstrapper.
7. **Bundle:** ranks candidates without excluding in-flight areas. **True:** the issue's own second
   comment excludes `CaptureService` and `DataExportService` from selection while Context Fabric
   settles. **Consequence:** REF-1's nomination step needs an explicit "active-wave" exclusion input,
   or the ranking will keep nominating files nobody may touch.
8. **Bundle:** "Legacy skin retirement boundary" listed as a decision the measurement informs.
   **True:** `#2141` records that the transcript loop is not completable in one skin — a product
   decision. **Consequence:** it belongs in the maintainer decision list, not in REF-1's output.
