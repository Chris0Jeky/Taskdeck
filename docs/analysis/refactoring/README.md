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

The report carries two separate provenance facts, because neither can stand in for the other:

- `sourceStateAuthoritative` is true when the tracked tree was clean, so every ranked number came
  from the exact `headCommit` objects with no working-tree drift mixed in.
- `baseRefKind` records what the baseline actually was (`tag`, `branch`, `remoteBranch`, `commit`,
  `head`, or `other`). A milestone measurement additionally requires `baseRefKind` to be `tag` and
  that tag to be the milestone's final release tag; the tool cannot decide which tag that is, so a
  clean run against a branch or an RC is a valid receipt of an exploratory measurement, never a
  milestone one.

By default the command refuses tracked changes. `--allow-dirty` is available for local exploration; when tracked changes exist, the report sets `sourceStateAuthoritative: false`. Untracked files do not affect the tracked-source calculation and do not block a run.

`--json-out` and `--csv-out` must resolve to different paths; a shared destination is rejected before either file is written, so the JSON receipt cannot be silently replaced by the CSV.

Every Git read ignores local replacement objects, and the command rejects legacy graft metadata.
The report records that object policy so local `.git` metadata cannot silently rewrite the receipt's
tree or history while retaining the original commit identifiers.

The score is:

```text
ln(1 + lines) * ln(1 + churn) * sqrt(max(1, touchingCommits))
```

- `lines` is the physical line count read from the exact `headCommit` Git blob. Working-tree
  content is never mixed into an exact-SHA receipt, including with `--allow-dirty`.
- `churn` is additions plus deletions in `BASE..HEAD`.
- `touchingCommits` is the number of distinct commits in that range that touched the path.
- Rename history is followed through Git's rename detection and attributed to the current path.

Attribution reads `BASE..HEAD` once with `git log --reverse --topo-order --raw --numstat`, and
builds file lineages rather than a one-way alias map. A rename links the old and the new name as an
undirected pair, so a sibling branch that edits the old name is attributed to the same file whether
Git happens to linearise that edit before or after the rename. Any event that creates a new occupant
at an already-seen path -- an add, a copy, or a rename onto it -- opens a new lineage, so a path that
is deleted and later reused does not inherit the previous file's history.

Every Git invocation neutralises `core.attributesFile`, so a contributor's global attributes cannot
decide whether a source file is diffed as text and thereby change reported churn. In-tree
`.gitattributes` remains in force because it is repository policy rather than local configuration.

Ties sort by churn, lines, then repository-relative path, so the same repository state produces byte-stable JSON when written with the same options. The report also records the Git version because rename detection can vary between Git releases.

## Boundaries

The ranker includes common Taskdeck source extensions and excludes dependency, build, coverage, migration, generated, lock, minified, binary, symlink, unreadable, and oversized files. The JSON report records the exact extension and exclusion policy used.

Git rename detection is heuristic. Copy history is intentionally not followed. Merge commits are
excluded from churn aggregation so their first-parent delta does not recount merged branch commits;
conflict resolution authored only in the merge may therefore be absent. Lineage generations are
assigned from the linearised commit order, so a path independently created on two branches and then
merged keeps only the later-visited lineage; that shape is genuinely ambiguous in a DAG. Tracked
paths that are not valid UTF-8 cannot be ranked and are counted in
`summary.excludedUndecodableTrackedPaths` instead of aborting the report. A large or frequently
edited file can also be cohesive and healthy. These limits are why the score cannot establish a
refactoring requirement by itself.

After the final `v0.3.0` tag:

1. Run the tool from a clean tracked tree and retain its exact-SHA JSON receipt.
2. Manually inspect the top 20 for responsibility count, coupling, generated-content leakage, and user-facing relevance.
3. Select no more than five seams with a stated behavior or maintenance problem.
4. Create one issue and one characterization-test-first PR per seam.
5. Re-measure the behavior or maintenance outcome after each change; do not optimize the rank score itself.

Generated reports are evidence artifacts. Do not commit a provisional baseline as if it were the final `v0.3.0` measurement.
