# Refactoring measurement

Taskdeck ranks refactoring candidates from repository evidence before opening implementation work. The rank is an inspection queue, not an automatic refactoring decision.

The initial method came from the 2026-08-30 v0.4 acceleration bundle. The repository implementation in `scripts/analysis/rank_refactor_candidates.py` hardens that candidate with exact commit provenance, tracked-tree checks, deterministic output, rename-aware churn attribution, bounded source-file handling, and tests.

## Run the ranker

Use the final exact release tag as the baseline for an authoritative milestone report:

```powershell
py -3 -B scripts/analysis/rank_refactor_candidates.py `
  --repo . `
  --base v0.3.0 `
  --top 20 `
  --json-out artifacts/refactoring/v0.3.0-to-head.json `
  --csv-out artifacts/refactoring/v0.3.0-to-head.csv
```

Until the exact `v0.3.0` tag exists, runs against an RC, moving branch, or older tag are exploratory only. Record the emitted `baseCommit`, `headCommit`, and tracked-tree status with any discussion of results.

By default the command refuses tracked changes. `--allow-dirty` is available for local exploration; when tracked changes exist, the report sets `authoritative: false`. Untracked files do not affect the tracked-source calculation and do not block a run.

The score is:

```text
ln(1 + lines) * ln(1 + churn) * sqrt(max(1, touchingCommits))
```

- `lines` is the current physical line count.
- `churn` is additions plus deletions in `BASE..HEAD`.
- `touchingCommits` is the number of distinct commits in that range that touched the path.
- Rename history is followed backwards from each current path using Git's rename detection.

Ties sort by churn, lines, then repository-relative path, so the same repository state produces byte-stable JSON when written with the same options. The report also records the Git version because rename detection can vary between Git releases.

## Boundaries

The ranker includes common Taskdeck source extensions and excludes dependency, build, coverage, migration, generated, lock, minified, binary, symlink, unreadable, and oversized files. The JSON report records the exact extension and exclusion policy used.

Git rename detection is heuristic. Copy history is intentionally not followed. Merge-only conflict resolutions may not appear as a separate per-parent delta. A large or frequently edited file can also be cohesive and healthy. These limits are why the score cannot establish a refactoring requirement by itself.

After the final `v0.3.0` tag:

1. Run the tool from a clean tracked tree and retain its exact-SHA JSON receipt.
2. Manually inspect the top 20 for responsibility count, coupling, generated-content leakage, and user-facing relevance.
3. Select no more than five seams with a stated behavior or maintenance problem.
4. Create one issue and one characterization-test-first PR per seam.
5. Re-measure the behavior or maintenance outcome after each change; do not optimize the rank score itself.

Generated reports are evidence artifacts. Do not commit a provisional baseline as if it were the final `v0.3.0` measurement.
