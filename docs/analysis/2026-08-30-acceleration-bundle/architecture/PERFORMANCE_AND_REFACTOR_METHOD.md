> **Validated 2026-09-02 against `main` `de488fea0`.**
>
> - **The score is adopted**, in the issue and in RECONCILIATION.md: `ln(1 + lines) x ln(1 + churn) x sqrt(max(1, touching_commits))`. Nothing below changes that.
> - **REF-0 is in flight and PARKED, not unstarted.** PR **#2356** (`issue-2236/refactor-measurement-tooling`, head `c3c04a26b`, +747/-0 across `scripts/analysis/rank_refactor_candidates.py`, its test file and `docs/analysis/refactoring/README.md`) sits at the two-round review ceiling on a confirmed HIGH: rename attribution depends on traversal order. Neither `scripts/analysis/` nor `docs/analysis/refactoring/` exists on `main`.
> - **The bundled ranker has defects this method text does not warn about:** `git log --numstat` with no `--no-merges` (merge commits double-count churn in a merge-commit-only history), **no rename handling at all**, `line_count()` returning `0` on any decode or OS error (a swallowed failure), substring path exclusions that also drop `.../robin/x.cs` for the `bin/` rule, and line counts read from the working tree rather than the exact commit tree — which matters here because `.worktrees/` holds ~70 preserved checkouts.
> - **Two of the top-ranked seams are off-limits right now.** `#2236`'s own reconciliation excludes `CaptureService.cs` and `DataExportService.cs` from selection while Context Fabric settles; ranking must take an explicit active-wave exclusion input.
> - **Benchmarking is not greenfield, and it is not as bare as "existing k6 and bundle budgets" suggests.** `.github/workflows/reusable-performance-regression-gate.yml` drives `tests/load/k6/board-heavy-load.js` with `scripts/ci/check-k6-thresholds.mjs`, `require-k6-summary.mjs` and a `k6-summary-contract.mjs` summary contract, plus `scripts/ci/check-bundle-size.mjs` (eager-graph budget 1250 KB). A new result schema should extend that contract's vocabulary, not compete with it. `benchmarks/`, `scripts/performance/` and `docs/performance/` do not exist.
> - **Live thresholds are the hand-set numbers to re-derive:** `http_req_duration p(95)<2000 / p(99)<5000`, `{workload:board-read} p(95)<900`, `{workload:board-write} p(95)<4500`, calibrated 2026-07-23 under `#1449`.
> - **The export scenario has a confirmed target.** `DataExportService.StreamUserDataExportAsync` (line 401) does stream, but lines 1164/1186/1212 pass `limit: int.MaxValue` for proposals, chat sessions and audit logs — measure allocation and RSS there rather than restating the bundle's stale DATA-003 premise.
> - **Scope fences from the live issues:** database-file / WAL work belongs to `#1166` and `#2238`; CI feedback time belongs to `#2324` and its CI children. Neither is this method's business.
>
> The body below is the bundle text, unedited.

# Performance and refactor method

## Refactor ranking

Use an explainable candidate score, not an automatic refactor command:

```text
score = ln(1 + lines) × ln(1 + churn) × sqrt(max(1, touching_commits))
```

- `lines`: current non-generated source lines;
- `churn`: additions + deletions since the selected baseline/tag;
- `touching_commits`: distinct commits touching the path.

Exclude generated, vendor, lock, migration snapshot and binary files. Inspect the top 20 manually for coupling and responsibility count. Pick five seams; each gets characterization tests and a separate issue/PR.

## Benchmark protocol

Each result contains:

- Taskdeck tag/SHA and dirty-tree state;
- OS/kernel, CPU, RAM, disk, runtime, browser, power plan;
- dataset generator version and checksum;
- cold/warm protocol;
- warm-up and measured repetitions;
- raw samples plus P50/P95/max;
- background-process notes;
- failure count.

## Scenarios

### API core loop

- capture → triage → proposal → apply;
- 1, 10 and 100 boards;
- deterministic fake LLM for baseline, live provider separately.

### SQLite

- 10k and 100k cards;
- board load, list, search, review queue, export;
- cold and warm cache where feasible.

### Realtime/MCP

- SignalR fan-out to 2 and 10 clients;
- MCP list/read latency over HTTP and stdio;
- process startup separated from steady-state calls.

### Frontend

- packaged build Lighthouse;
- route/eager bundle sizes;
- 500-card board render and interaction;
- one stable browser version.

## Candidate ranking

For each bottleneck:

```text
priority = expected gain × confidence × user frequency / (implementation size × regression risk)
```

File the top three with the exact scenario/query that proves the problem and the target that would close it. Do not optimize a metric that is not tied to user or operator behavior.
