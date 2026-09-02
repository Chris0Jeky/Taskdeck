# CI-12 — CI receipts and the weekly cost / critical-path / flake report (#2336)

Last Updated: 2026-09-02

> Curated from the v0.3/v0.4 acceleration bundle (grounded `221aa88c8`, 2026-08-30) and validated against `main` `de488fea0` on 2026-09-02 under tracker #2348 follow-up. Planning input, not authority: the live issue, ADR-0066 §Decision 11, `ci/schemas/ci-run.v1.schema.json` as it exists on `main`, and `docs/ci/SMART_CI.md` §8 win. Corrections to the bundle are in the last section.

## Outcome

Make CI economics queryable: extend the receipt that already ships so every gate run records per-job
timing, runner class, allowance minutes, test counts, reruns, cache and artifact bytes, then derive
one reproducible weekly report from those receipts plus Actions API metadata. Content-free, unit-explicit,
and one ledger — not a second one beside the landed gate.

## Live dependencies (verified 2026-09-02)

| Issue | State | Must deliver first | Unblocks |
| --- | --- | --- | --- |
| CI-02 `#2326` planner | open (shadow scaffold merged) | `plan.mjs` produces the plan the receipt embeds; `policyDigest`, `risk`, `trust`, `selected`, `skipped` already flow through | 01–04 |
| CI-03 `#2327` gate | open (evaluator merged) | **`evaluate-gate.mjs` already writes `artifacts/ci-run.json`.** This issue extends that writer; it does not create one | 01–04 |
| CI-09 `#2333` retention | open | Receipts are uploaded as artifacts with **14-day retention** today. A "weekly" report over a 14-day window is fine; anything longitudinal needs a retention decision first | 03 |
| CI-15 `#2339` flakes | open | Consumes the receipt's rerun/first-failure fields. `#2339` depends on this issue, so the *field shape* must be frozen here | 04 |
| CI-01 `#2325` baseline | **closed** | `docs/ci/CI_BASELINE.md` fixes the cost method: per-job round-up, Windows x2, macOS x10, Linux 2-core $0.006/min, Windows $0.010/min, verified on docs.github.com 2026-08-30 | 01, 03 |

## Child slices (one PR each, in order)

| Id | Outcome | Depends on | Mode | Startable before predecessors merge? |
| --- | --- | --- | --- | --- |
| `CI12-1-schema` | Extend `ci/schemas/ci-run.v1.schema.json` with `jobs[]` and `summary` — every field unit-suffixed, every optional field with a stated absent-semantics — and extend `evaluate-gate.mjs` to write them | — | control-plane (R4/T2) | **Yes — start here.** The schema and its writer are both on `main`; this is an additive extension the schema's own `description` already anticipates by name |
| `CI12-2-emit` | The gate collects real job evidence (`--results`) from the Actions API and validates the receipt against the schema before upload | 01 | control-plane | No — needs the extended field set to have something to validate |
| `CI12-3-report` | `scripts/ci/smart-ci/weekly-report.mjs` + `node --test`: receipts in, Markdown/JSON out, byte-stable for a fixed input set | 01 | tooling | **Partly** — the report's pure aggregation functions and their fixtures are startable now against the frozen field names |
| `CI12-4-budgets` | Per-lane budget warnings naming lane, observed P95, budget, delta and reason; repeated flakes open or update an owned finding | 03 | tooling + control-plane | No |

## Architecture

| Concern | Type | Status | Note |
| --- | --- | --- | --- |
| Receipt schema | `ci/schemas/ci-run.v1.schema.json` | **exists** | `additionalProperties: false`, `schemaVersion: 1`, camelCase, `$id` `https://taskdeck.dev/schemas/ci-run.v1.schema.json`. Its own description says: *"CI-12 #2336 extends it with per-job timing, allowance-minute, test-count, flake and storage fields"* — this issue is named in the shipped artefact |
| Receipt writer | `scripts/ci/smart-ci/evaluate-gate.mjs` (lines 89–113) | **exists** | Writes 20 fields today: verdict, failures, notes, policy id/digest, event, base/head/merge/mergeTree SHA, risk, trust, escalated, selected[], skipped[], generatedAtUtc. **No timing, no cost, no test counts, no cache, no artifacts** |
| Receipt transport | `.github/workflows/smart-ci-shadow.yml` → `upload-artifact` `smart-ci-receipt-<pr>-<sha>` | **exists** | `retention-days: 14`, `if-no-files-found: ignore`. Receipts are per-PR artifacts; nothing aggregates them |
| Job evidence | `evaluateGate(..., { results })` in `scripts/ci/smart-ci/lib/plan.mjs:468` | **exists, unwired** | The evaluator already handles a `results` map with `conclusion` + `headSha` and emits `selected-evidence-missing` / `evidence-wrong-sha`. The shadow workflow **never passes `--results`**, so job evidence is not collected yet — CI12-2's real work |
| Plan receipt | `ci/schemas/ci-plan.v1.schema.json` + `validatePlan()` | **exists** | Validation is executable JS, not a schema library — `ci/README.md` records this as a deliberate open decision shared with the processor manifest (CF-04). Keep the same style |
| Cost method | `docs/ci/CI_BASELINE.md` §Method | **exists** | The rate table, the per-job round-up and the **unverified** Windows x2 / macOS x10 allowance assumption. Any `hostedCostEstimate` field must carry the rate-table version and repeat that caveat |
| Weekly report | — | **new** | `docs/ci/SMART_CI.md` §11 names `scripts/ci/smart-ci/weekly-report.mjs`; it does not exist |
| Aggregation store | — | **new, and the real design question** | See below |

**Where receipts live is the unsolved part.** Today each receipt is a 14-day PR artifact. A weekly
report can download them through the Actions API, which is fine and needs no new storage — but it
caps history at retention and costs API calls. Committing receipts to the repository turns CI output
into repository history and grows the tree forever. Recommend: report from the API over a rolling
window, and commit only the *derived* weekly summary under `docs/ci/reports/` — the same
append-a-dated-ledger discipline `docs/ci/CI_BASELINE.md` already uses for the estate.

**Fail-open is the failure mode to design against.** Every aggregate in the bundle's reporter uses
`.get(field, 0)`, so a receipt missing a field reports `0.0` rather than an error. A cost report
that silently reads zero is worse than no report. Absent fields must be `null` with an explicit
"insufficient data" row, never zero.

## Implementation plan

**Preflight.** Read `ci/schemas/ci-run.v1.schema.json` and `evaluate-gate.mjs:89-113` side by side;
the extension must keep `additionalProperties: false` honest by adding each new field to both at
once, or every receipt fails its own schema.

**Producer-owned paths:** `ci/schemas/ci-run.v1.schema.json`, `scripts/ci/smart-ci/evaluate-gate.mjs`,
`scripts/ci/smart-ci/lib/plan.mjs` (`validateReceipt` beside `validatePlan`),
`scripts/ci/smart-ci/weekly-report.mjs` + `weekly-report.test.mjs`, fixtures beside them.

**Integration-owner seams:** `.github/workflows/smart-ci-shadow.yml` (adding the `--results`
collection step and a scheduled report job), `ci/README.md`, `docs/ci/SMART_CI.md` §8,
`docs/STATUS.md` §CI Status.

**Rollout / rollback.** Three stages, matching the issue's own "staged rollout": (1) emit the new
fields, validation advisory; (2) validation red on a malformed receipt; (3) **only then** a missing
receipt fails the gate. Stage 3 must not land while the gate is still in shadow — `ci/policy.v1.json`
`mode: shadow` means a red gate today is a planner defect, and making absence fatal in that state
would produce exactly the false reds CI-03's 20-PR observation window is counting.

**Definition of done.** The weekly report is byte-stable: running it twice over the same receipt set
produces identical output (no timestamps in the body, sorted keys). Every number carries a unit in
its field name (`...Seconds`, `...Minutes`, `...Bytes`) and every currency figure carries the
rate-table version. No receipt field can contain a test name's failure message, a log line, a
branch name typed by a user, or a file path from a diff — names, ids, counts, timestamps and sizes only.

## Test plan

- [ ] Schema: the receipt `evaluate-gate.mjs` writes validates against the extended schema — a round-trip fixture test, `node --test scripts/ci/smart-ci/*.test.mjs`
- [ ] Schema: an unknown property is rejected (`additionalProperties: false` still enforced after the extension)
- [ ] Schema: a numeric field without a unit suffix fails a naming lint in the test, not review taste
- [ ] Receipt: a cancelled job yields `result: cancelled` with `null` timings, never `0`
- [ ] Receipt: a self-hosted job records wall seconds and **no** hosted cost; a hosted job records both
- [ ] Receipt: allowance minutes round each job up to a whole minute and multiply Windows by 2 — assert against a fixture reproducing a row of `docs/ci/CI_BASELINE.md`
- [ ] Report: critical path = last job completion − first job start within one run, matching the baseline's definition exactly
- [ ] Report: P50/P95 with a sample below the declared minimum emits "insufficient sample (n=N)", not a number
- [ ] Report: two receipts for one head SHA from **rerun attempts** are *not* counted as duplicate qualification; two receipts from two independent workflow runs are (correction 4)
- [ ] Report: byte-stable — same input set, two runs, identical output
- [ ] Report: a receipt missing `summary` is listed as excluded with a reason; the totals do not silently absorb it
- [ ] Budget: an R2 lane at 12 minutes against a 10-minute budget produces a warning naming lane, observed P95, budget, delta
- [ ] Docs: `node scripts/check-docs-governance.mjs`

## Edge cases

- A job that never started (dependency skipped): no timings at all — distinguish `skipped-with-reason` from `missing`.
- Self-hosted job with unknown cost: cost must be `null` with a reason, never `0.00`.
- A rerun of one failed job inside an otherwise green run: attempt numbering must be recorded or the flake ledger cannot tell a rerun from a new run.
- A receipt uploaded twice for the same PR head (the shadow workflow's artifact name is `<pr>-<sha>`, so a re-synchronize on the same SHA overwrites).
- Receipts aged out of the 14-day retention mid-window → the report's denominator changes; state the window and the count of receipts actually read.
- Zero selected lanes (an R0 docs-only PR): the report must not divide by zero computing yield.
- The `hostedCostEstimate` rate table changes (GitHub repricing) — old receipts stay valid; the report must group by rate-table version rather than mixing.
- Very small P95 samples in a low-traffic week — the issue's own "P95 sample minimum" decision.

## Reference material

| Kind | Archived path | Good for | Caveats |
| --- | --- | --- | --- |
| Receipt schema | `docs/analysis/2026-08-30-acceleration-bundle/candidates/smart-ci/ci-run.v1.schema.json` | The **field inventory** to mine: `jobs[].{runner_class,hosted,queue_seconds,setup_seconds,test_seconds,total_seconds,result,tests_*,rerun,cache_hit,artifact_bytes}` and `summary.{critical_path_seconds,aggregate_runner_seconds,hosted_minutes,hosted_cost_estimate,self_hosted_wall_seconds,flake_detected}` | **Incompatible with `main`.** Same filename, same "v1", different `$id`, snake_case, different required set, `additionalProperties: false` on both — nothing can satisfy both. Adopt the field *names' meaning*, re-spelled in the shipped camelCase. Never copy the file (correction 1) |
| Reporter | `.../candidates/smart-ci/weekly_ci_report.py` | Metric list, nearest-rank percentile, lane-yield table, duplicate-SHA grouping | Reads the incompatible shape; fails open to `0.0` on every missing field; counts `skipped` as success; treats every repeated head SHA as duplicate qualification (corrections 2–4). Python, while the shipped lane is `node --test` `.mjs` |
| Test vector | `.../testing/test-vectors/ci-run.sample.json` | A complete example of the bundle's receipt shape | Same incompatibility; useful only as a fixture for the bundle's own reporter |
| Docs draft | `.../docs-drafts/CI_WEEKLY_REPORT_TEMPLATE.md` | **Directly usable.** Outcomes list, budget-regression table, flake/quarantine table, slow-test table, and the line "Skip more jobs is not an outcome by itself" | Needs the rate-table-version column made mandatory |
| Diagram | `.../diagrams/smart-ci-control-loop.svg` | Receipt → analysis → weekly evidence → policy adjustment | Explanatory only |

## Corrections to the bundle

1. **Bundle:** ships `ci-run.v1.schema.json` as the receipt contract to adopt. **True on `main`:**
   `ci/schemas/ci-run.v1.schema.json` **already exists** (merged with the Smart CI shadow control
   plane) and is *structurally incompatible* with the bundle's file of the same name and version:
   `$id` `taskdeck.dev` vs `taskdeck.local`; camelCase (`schemaVersion`, `headSha`, `policyDigest`)
   vs snake_case (`schema_version`, `head_sha`, `policy.digest`); required
   `[schemaVersion, kind, mode, ok, wouldFail, failures, notes, policyId, policyDigest, event,
   baseSha, headSha, mergeSha, mergeTreeSha, risk, trust, escalated, selected, skipped,
   generatedAtUtc]` vs `[schema_version, run_id, repository, workflow, attempt, head_sha, policy,
   risk, selected, skipped, jobs, summary]`; and both set `additionalProperties: false`.
   **Consequence:** copying the bundle's file would replace the schema the landed gate writes
   against and break `evaluate-gate.mjs` at the same path and version number — the "do not fork the
   M4 control plane" rule the bundle itself states. Mine it for fields only.
2. **Bundle:** `weekly_ci_report.py` is presented as the report implementation. **True:** every
   aggregate is `float(doc.get('summary', {}).get(<field>, 0))`, so against a *shipped* receipt —
   which has no `summary` and no `jobs` — the report prints `0.0` for critical path, cost, queue and
   cache and `0.0%` yield, with no error. **Consequence:** fail-open reporting on a cost ledger.
   Absent must be `null` + an excluded-receipt row.
3. **Bundle reporter:** counts a lane as successful when `job.result in {success, skipped}`.
   **True:** the shipped gate has a distinct failure code `skipped-without-reason`. **Consequence:**
   the report would hide exactly the condition the gate is built to catch; skipped must be its own
   column.
4. **Bundle reporter:** `duplicate_shas = shas seen more than once`. **True:** a normal PR emits one
   receipt per `synchronize`/`labeled`/`edited` event, and the shadow workflow's artifact key is
   `<pr>-<head sha>`, so several receipts per head SHA are the expected case; the bundle's own schema
   has an `attempt` field it then ignores. **Consequence:** duplicate *qualification* must be defined
   as two independent successful **workflow runs** of the same lane at the same SHA, not two receipts.
5. **Bundle:** "Every gate run emits a validated `ci-run.json`" is listed as new work, and the live
   issue's first acceptance box says "a missing receipt fails the gate". **True:** emission already
   happens on every shadow run; what is missing is the *field depth*, the `--results` job-evidence
   collection, and schema validation. **Consequence:** re-scope acceptance box 1 to "extended
   receipt schema-validated", and hold "missing receipt fails the gate" until `ci/policy.v1.json`
   leaves `mode: shadow` (correction 6).
6. **Bundle:** "Make emission required before making selective execution required." **True and
   sharper:** `ci/policy.v1.json` is `mode: shadow`, and in shadow the gate is red only for
   planner/plan defects. **Consequence:** a "missing receipt is red" rule landed during shadow
   manufactures false reds against CI-03's 20-PR observation window — the one thing that window
   measures. Sequence it after registration.
7. **Bundle:** file ownership lists `docs/ci/**` and `ci/schemas/ci-run.v1.schema.json`. **True:**
   both exist; add `ci/README.md`, which documents the schema table and would go stale, and
   `scripts/ci/smart-ci/lib/plan.mjs`, where the executable validators live.
8. **Bundle:** "Hosted cost table source/version" is an open decision. **True:** `docs/ci/CI_BASELINE.md`
   §Method already fixes the table, the date it was read, and one **explicitly unverified**
   assumption (Windows x2 / macOS x10 allowance multipliers). **Consequence:** the decision is
   "carry the baseline's table and its caveat as a versioned field", not "choose a source".
