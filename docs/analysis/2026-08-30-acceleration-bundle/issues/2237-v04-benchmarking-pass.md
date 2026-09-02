# PERF — v0.4 benchmarking pass and ranked improvement candidates (#2237)

Last Updated: 2026-09-02

> Curated from the v0.3/v0.4 acceleration bundle (grounded `221aa88c8`, 2026-08-30) and validated against `main` `de488fea0` on 2026-09-02 under tracker #2376 (follow-up to #2348). Planning input, not authority: the live issue and its two comments, `docs/HARDENING_AND_PERFORMANCE.md` and the shipped k6 gate win. Corrections to the bundle are in the last section.

## Outcome

One checked-in, one-command benchmark suite whose every result carries tag/SHA, machine manifest,
dataset checksum, warm/cold protocol, repetitions and P50/P95 — then a `v0.3.0`-tag baseline with the
raw numbers committed, three ranked candidates each backed by the measurement that proves it, and
nightly k6 thresholds re-derived from the baseline instead of hand-set.

## Live dependencies (verified 2026-09-02)

| Item | State | Relationship |
| --- | --- | --- |
| `v0.3.0` tag | not yet cut (`v0.3.0-rc.1`, 2026-08-30) | PERF-3's authoritative numbers wait for it; PERF-1 and PERF-2 do not |
| `#2236` REF | open, REF-0 PR #2356 parked | Sibling lane. Both measure the same code; keep the harnesses separate but reuse the *machine manifest* shape |
| `#1133` | open | Named perf items: NotificationRepository paging, incremental SignalR board patch instead of a 3-call refetch, FTS-backed search |
| `#1355`, `#1467` | open | Export blob batching; review-list snapshot bound |
| `#1166`, `#2238` | open / closed (`#2238` via PR #2361) | Database-file / WAL work routes there, **not** here (the issue's first comment) |
| `#2324` and CI children | open | CI feedback-time work routes there, **not** here |
| `#1449` | closed | The k6 recalibration whose thresholds this baseline must re-derive |

Nothing blocks PERF-1 or PERF-2.

## Child slices (one PR each, in order)

| Id | Outcome | Depends on | Mode | Startable before predecessors merge? |
| --- | --- | --- | --- | --- |
| `PERF-1-contracts` | The result schema and the scenario contract: machine manifest, dataset generator + checksum, warm/cold definition, warm-up and measured repetitions, percentiles, failure count | — | contract-only | **Yes — start here.** It is a JSON schema plus a Markdown contract; it changes no product code and it is what stops PERF-2 from producing unusable numbers |
| `PERF-2-harness` | The runnable scenarios emitting content-free raw JSON against the schema: API core loop, SQLite at scale, SignalR fan-out, MCP list/read, frontend packaged build | 01 | tooling | **Partly** — individual scenario scripts are startable; do not commit any result before the schema is frozen |
| `PERF-3-baseline` | Run from the exact `v0.3.0` tag on the stated box; commit manifest + raw data + summary; derive thresholds | 02 **and** the tag | measurement | No — two hard predecessors |
| `PERF-4-ranking` | `expected gain x confidence x user frequency / (size x regression risk)`; file the top three with the exact proof query | 03 | analysis | No |

## Architecture

| Concern | Type | Status | Note |
| --- | --- | --- | --- |
| Nightly performance gate | `.github/workflows/reusable-performance-regression-gate.yml` + `scripts/ci/check-k6-thresholds.mjs` (+ `.test.mjs`) + `scripts/ci/require-k6-summary.mjs` + `scripts/ci/k6-summary-contract.mjs` | **exists** | The shipped harness. It already enforces a summary *contract* — PERF-1's schema should extend this idea, not compete with it |
| k6 scenario | `tests/load/k6/board-heavy-load.js` | **exists** | Thresholds today: `http_req_duration p(95)<2000, p(99)<5000`; `{workload:board-read} p(95)<900`; `{workload:board-write} p(95)<4500`. Comments record the 2026-07-23 tail calibration — these are the hand-set numbers the issue wants re-derived |
| Bundle budget | `scripts/ci/check-bundle-size.mjs` | **exists** | Eager-graph JS budget **1250 KB** (`BUNDLE_MAX_EAGER_JS_KB`), the `#1858` ruling; measured 402 KB eager. History: 1200 → 1250 (2026-08-19, `#1770`), a 1280 stopgap on 2026-08-22 |
| Perf documentation | `docs/HARDENING_AND_PERFORMANCE.md` | **exists** | The prose home; the baseline is a new document beside it |
| Streaming export | `DataExportService.StreamUserDataExportAsync` (line 401) | **exists** | Streams JSON bytes — the bundle's "DATA-003" premise is stale |
| Unbounded export sections | `DataExportService.cs` lines 1164–1212 | **exists — confirmed** | Three `limit: int.MaxValue` calls, with the comment "so we cannot page at the DB level. Load all rows in a single query using int.MaxValue". Proposals, chat sessions and audit logs materialize whole. This is the measurable allocation/RSS risk the issue's second comment names |
| `benchmarks/**`, `scripts/performance/**`, `docs/performance/**` | — | **none exist** | Verified: no such directories on `main`. The bundle's suggested ownership paths are all greenfield |
| Result schema | — | **new** | The single most valuable early artefact |

**Coverage gap, stated precisely.** The shipped gate covers *board-heavy API load under k6* and
*frontend bundle size*. It does not cover: the capture → triage → proposal → apply loop, SQLite at
10k/100k cards, review-queue and search latency, SignalR fan-out, MCP list/read, packaged route
mount, or export/backup at scale. Those seven are the harness's actual scope.

**Budgets must be dataset- and hardware-scoped.** A universal "board load < 900 ms" claim is
meaningless across a CI runner and the maintainer's box. Every threshold the baseline derives carries
its dataset id and its machine manifest hash, and the nightly gate keeps its own CI-runner-scoped set.

## Implementation plan

**Preflight.** Read `scripts/ci/k6-summary-contract.mjs` and `check-k6-thresholds.mjs` before
designing the result schema — reusing their summary vocabulary means the nightly gate can consume
baseline-derived thresholds without a translation layer.

**Producer-owned paths:** `benchmarks/**`, `scripts/performance/**`, `docs/performance/**`, and
`tests/load/k6/**` for new scenario profiles.

**Integration-owner seams:** `.github/workflows/reusable-performance-regression-gate.yml` and
`scripts/ci/check-k6-thresholds.mjs` — **only after** the baseline exists (the bundle's own "`.github/workflows/**`
only after baseline" rule, which is correct). Touching the live threshold file before then edits a
gate against no evidence.

**Rollout / rollback.** No product behaviour changes anywhere in PERF-1..3. PERF-4 files issues; it
does not implement optimizations. Rollback for a threshold change is restoring the previous
committed values, which is why they must be in the repository and not in workflow inputs alone.

**Definition of done.** `docs/performance/BASELINE_v0.3.md` exists with the raw numbers committed
alongside it, states the box, and is reproducible: a second run on the same tag and box lands inside
the recorded variance. Thresholds in `check-k6-thresholds.mjs` cite the baseline row they came from.
No measurement uses production data. Nothing claims a number the issue's own protocol did not produce.

## Test plan

- [ ] Harness self-check: the harness runs end to end against a seeded database and produces a schema-valid result document — `node --test scripts/performance/*.test.mjs` (or the chosen runner)
- [ ] Dataset: the generator is deterministic and its checksum is recorded in the result; regenerating produces the same checksum
- [ ] Schema: a result missing tag/SHA, machine manifest, dataset checksum, repetitions or percentiles is **rejected**, not defaulted
- [ ] Schema: mean-only reporting is impossible — P50 and P95 are required fields
- [ ] Variance: two runs at the same tag on the same box land within the recorded variance band; the band itself is committed
- [ ] Threshold derivation: unit tests over the derivation function (percentile + margin), so a threshold cannot be hand-edited without failing a test
- [ ] Dirty tree: a run from a dirty worktree is recorded as dirty and refused as a baseline
- [ ] Export scenario: allocation/RSS measured across `StreamUserDataExportAsync` with a large seeded account, so the three `int.MaxValue` sections are quantified rather than asserted
- [ ] Nightly dry run: the existing gate stays green with the re-derived thresholds — `.github/workflows/reusable-performance-regression-gate.yml` on one nightly
- [ ] Bundle: `cd frontend/taskdeck-web; npm run build` then `node scripts/ci/check-bundle-size.mjs` — eager graph unchanged
- [ ] Docs: `node scripts/check-docs-governance.mjs`

## Edge cases

- SQLite page-cache and WAL state between runs — the cold/warm definition must say whether the file is dropped, and the `#1166`/`#2238` database-file work must not be conflated with it.
- Windows Defender / real-time scanning on this box materially affects SQLite and process-startup numbers; record whether it was on.
- CPU governor / power plan and thermal throttling on a laptop — record the plan and the run order.
- Background agents: this box routinely runs several Claude/Codex sessions and ~70 worktrees; a benchmark run must state what else was running.
- Port contention with a running dev stack.
- Browser version drift between Lighthouse runs — pin one version per baseline.
- LLM nondeterminism: the capture → proposal loop must use the deterministic fake provider for the baseline; a live provider is a separate, clearly-labelled scenario.
- A scenario that fails partway: failure count is a first-class field, and a partial run is never summarized as a fast one.

## Reference material

| Kind | Archived path | Good for | Caveats |
| --- | --- | --- | --- |
| Docs draft | `docs/analysis/2026-08-30-acceleration-bundle/docs-drafts/PERFORMANCE_BASELINE_TEMPLATE.md` | **Directly usable as PERF-1's document skeleton.** Build/machine block (tag, dirty tree, OS, CPU/power plan, RAM, disk, runtimes, antivirus, dataset hash), protocol block, and a results table already listing capture→proposal→apply, board load at 10k/100k, search, review queue, SignalR 2/10, MCP HTTP/stdio, 500-card render, Lighthouse | Add a variance column and a machine-manifest hash; the template records raw values but no reproducibility band |
| Blueprint | `.../architecture/PERFORMANCE_AND_REFACTOR_METHOD.md` §Benchmark protocol / §Scenarios / §Candidate ranking | The protocol field list and the `priority = expected gain x confidence x user frequency / (size x risk)` formula | See its validation preface |
| Diagram | `.../diagrams/milestone-5-dependency-graph.svg` | Milestone shape | Advisory only |
| Issue pack | Bundle `01_MILESTONE_5/issue-packs/2237.md` | The PERF-1..4 slice split, kept above; the "avoid" list (unlike hardware, mean-only, hand-set thresholds, production data, optimizing before profiler evidence) is all correct | Its "what exists" paragraph understates the shipped gate (correction 1) |

## Corrections to the bundle

1. **Bundle:** "Existing k6 and bundle budgets are useful but do not cover the full core loop…"
   **True and more specific:** the shipped gate is
   `.github/workflows/reusable-performance-regression-gate.yml` driving
   `tests/load/k6/board-heavy-load.js` with `scripts/ci/check-k6-thresholds.mjs`,
   `require-k6-summary.mjs` and a `k6-summary-contract.mjs` **summary contract**, plus
   `scripts/ci/check-bundle-size.mjs`. **Consequence:** PERF-1's result schema should extend the
   existing summary contract's vocabulary; a second, unrelated schema means the nightly gate cannot
   consume baseline-derived thresholds.
2. **Bundle:** treats `benchmarks/**`, `scripts/performance/**`, `docs/performance/**` as existing
   ownership candidates. **True:** none of the three directories exists on `main`.
   **Consequence:** all greenfield — which is good news for collision risk, and means PERF-1 also
   owns choosing where these live relative to the existing `tests/load/k6/`.
3. **Bundle:** its DATA-003 premise assumes export is not streamed. **True:**
   `DataExportService.StreamUserDataExportAsync` (line 401) streams JSON bytes — the issue's first
   comment already calls this premise "substantially stale". **But** the issue's *second* comment is
   the sharper truth and it is confirmed: lines 1164, 1186 and 1212 pass `limit: int.MaxValue`, with
   an in-code comment explaining that proposals, chat sessions and audit logs "cannot page at the DB
   level". **Consequence:** keep database-level section paging and allocation/RSS measurement in
   scope; if the baseline shows material growth, that becomes one of the three ranked candidates.
4. **Bundle:** "Reference hardware and power plan" listed as a decision to receive. **True:** the
   live issue already fixes it — "`docs/performance/BASELINE_v0.3.md` recorded against the `v0.3.0`
   tag **on this box** (specs stated)". **Consequence:** the open part is only *which* specs to
   record, and the bundle's own template answers that.
5. **Bundle:** "`.github/workflows/**` only after baseline". **True and worth hardening:** the
   current thresholds carry calibration comments dated 2026-07-23 and were recalibrated under
   `#1449`. **Consequence:** the PR that re-derives them must cite the baseline row per threshold, or
   it is another hand-set number with a new justification.
6. **Bundle:** silent on where database-file and CI-feedback work belongs. **Live issue comment:**
   database-file/WAL → `#1166`/`#2238`; CI feedback time → `#2324` and its CI children.
   **Consequence:** two explicit out-of-scope fences to restate in the PR, or this issue will absorb
   both lanes.
7. **Bundle:** "Threshold derivation method" as an open decision. **True:** partly answered by the
   live issue ("re-derived from the baseline rather than hand-set"). **Consequence:** what remains is
   the *margin* above P95 and the sample minimum — narrow those in PERF-1 rather than leaving the
   whole method open.
