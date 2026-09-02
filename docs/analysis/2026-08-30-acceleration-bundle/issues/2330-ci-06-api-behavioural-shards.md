# CI-06 — API behavioural shards and Windows process overhead (#2330)

Last Updated: 2026-09-02

> Curated from the v0.3/v0.4 acceleration bundle (grounded `221aa88c8`, 2026-08-30) and validated against `main` `de488fea0` on 2026-09-02 under tracker #2348 follow-up. Planning input, not authority: the live issue, ADR-0066, `docs/ci/SMART_CI.md` and `docs/STATUS.md` win. Corrections to the bundle — and to the issue body — are in the last section.

## Outcome

Cut the Windows API-integration bill without deleting coverage: first repair the fixed process
overhead in `backend/tests/Taskdeck.Api.Tests`, then partition the assembly into behavioural shards
whose union is provably the whole assembly, and let the CI-05 ownership map select shards. Sharding
a suite dominated by fixed waits only multiplies the waits — the order matters.

## Live dependencies (verified 2026-09-02)

| Issue | State | Must deliver first | Unblocks |
| --- | --- | --- | --- |
| CI-01 `#2325` baseline | **closed** | Delivered: `docs/ci/CI_BASELINE.md` + `docs/ci/baselines/ci-estate-2026-08-30.{md,json}`. This is the only measured "before" this issue may compare against | 01–04 |
| CI-05 `#2329` test-ownership manifest | open | `ci/test-ownership.v1.json` — production boundary → lanes. `docs/ci/SMART_CI.md` §11 already lists this path but **the file does not exist** in `ci/` on `main` | 04 |
| CI-02 `#2326` planner | open (scaffold merged) | `scripts/ci/smart-ci/plan.mjs` + `ci/policy.v1.json` are on `main` in **shadow** mode; the planner selects nothing yet | 04 |
| CI-03 `#2327` required gate | open (evaluator merged) | `scripts/ci/smart-ci/evaluate-gate.mjs` exists but is not passed `--results`, so no job evidence is checked yet | 04 |
| CI-12 `#2336` receipts | open | Per-job timing in the receipt is the only durable place a shard P95 can live | 02 (measurement), 04 |

Slices 01–03 have **no** blocking predecessor: they are test-project and harness work inside
`backend/tests/Taskdeck.Api.Tests/**`, which no Smart CI lane owns.

## Child slices (one PR each, in order)

| Id | Outcome | Depends on | Mode | Startable before predecessors merge? |
| --- | --- | --- | --- | --- |
| `CI06-1-inventory` | An assembly inventory extractor (`dotnet test --list-tests` or a reflection walk over the built assembly) plus `ci/api-shards.v1.json` and a union checker that fails on any class that is missing, duplicated, or unknown | — | tooling | **Yes — start here.** It is the cheapest true statement anyone can make about this suite, and it is the only way to write a shard manifest that is not fiction (see correction 1) |
| `CI06-2-harness` | Readiness probes replacing fixed sleeps, one prepared test binary per run, a central port reservation, deterministic cancel→grace→kill teardown, process logs captured on failure only | — | implementation | **Yes**, and it must land before any sharding. The measured Windows/Ubuntu delta is startup, polling and teardown, not semantics |
| `CI06-3-fixtures` | Share the expensive host fixture at an xUnit collection boundary only where an isolation test proves no cross-test state leaks | 02 | implementation | No — needs the deterministic teardown from 02 to be safe |
| `CI06-4-planner` | Shard lanes registered in `ci/policy.v1.json` with real check-context names; boundary ownership selects them; weekly/release run the full union | 01, CI-05 `#2329` | control-plane (R4/T2) | No — a lane whose `checkName` no workflow produces makes the gate fail closed on missing evidence |

## Architecture

| Concern | Type | Status | Note |
| --- | --- | --- | --- |
| The API suite under discussion | `backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj` | **exists** | **176** distinct `*Tests` classes. There is no `Taskdeck.Api.IntegrationTests` project anywhere in `backend/tests/` |
| How it runs today | `.github/workflows/reusable-api-integration.yml:53` | **exists** | One `dotnet test <csproj>` per matrix OS, no `--filter`, TRX logger `api-integration.trx`. Nothing to shard against yet |
| TRX timing history | `scripts/ci/summarize_trx_timing.py` (+ `test_summarize_trx_timing.py`) | **exists** | The shipped source of per-test timing; shard balancing must consume this, not a new parser |
| Runner context capture | `scripts/ci/collect_runner_context.py` | **exists** | The `#1512`/`#1521` artifacts the issue tells you to reuse |
| Measured platform delta | `docs/ci/CI_BASELINE.md` | **exists** | API Integration mean **17.3 min (windows) vs 7.2 min (ubuntu)** over 30 green runs = **2.4x**, 35.7 allowance minutes — the largest single line in the estate |
| Shard manifest + union checker | — | **new** | `ci/api-shards.v1.json` + a checker. The bundle's `validate_api_shards.py` is a usable *algorithm*; its inventory is not |
| Lane registration for shards | `ci/policy.v1.json` `lanes[]` | **exists, must be extended** | Each lane names a real `ci-required.yml` check context so the gate can verify it — a shard lane must add its workflow job in the same PR |

**Ordering invariant.** ADR-0066 §Decision 6 says the Windows MCP/process harness is *repaired*
rather than merely sharded. Read that as a hard sequence: 02 before 03 before 04. A shard split
landed before the harness repair converts one slow job into N slow jobs and raises allowance
minutes, because private-repository accounting rounds **every job** up to a whole minute and
multiplies Windows by 2 (`docs/ci/CI_BASELINE.md` §Method).

## Implementation plan

**Preflight.** Run the inventory extractor and diff it against the seven proposed shard names.
Expect to rewrite the manifest from scratch: none of the bundle's ten class names exist. Then read
one real TRX from a recent green run before claiming which classes are slow.

**Producer-owned paths:** `ci/api-shards.v1.json`, the union checker under `scripts/ci/` with its
`node --test` or `unittest` sibling, `backend/tests/Taskdeck.Api.Tests/**` (fixtures, collection
attributes, port reservation, teardown).

**Integration-owner seams** (one CI control-plane owner, R4/T2, hosted-only):
`ci/policy.v1.json`, `.github/workflows/reusable-api-integration.yml`, `.github/workflows/ci-required.yml`,
`docs/ci/SMART_CI.md` §5, `docs/STATUS.md` §CI Status (line 729).

**Rollout / rollback.** The union checker runs advisory first (report only) for at least one week
of PRs, then becomes red. Shard *execution* stays behind the same shadow discipline as selection:
run the shards **in addition to** the full union until one weekly sweep proves the union is exact,
then drop the unsharded job. Rollback is deleting the shard matrix from the reusable workflow; the
manifest itself is inert data.

**Definition of done.** The Windows P95 improvement is measured by re-running
`node scripts/ci/smart-ci/measure-ci-estate.mjs` over a post-change window and **appending** a new
dated ledger under `docs/ci/baselines/` — never overwriting `ci-estate-2026-08-30.json`. No timeout
value anywhere increases in the same PR (grep the diff for `timeout-minutes` and
`--blame-hang-timeout`).

## Test plan

- [ ] Union checker: inventory extracted from the built assembly equals the manifest union — `node --test scripts/ci/<checker>.test.mjs` (or `py -3 -B -m unittest`)
- [ ] Union checker: a class present in the assembly but absent from the manifest is **red**, naming the class
- [ ] Union checker: a class assigned to two shards is **red**; a manifest entry with no matching class is **red**
- [ ] Union checker: a parameterized/`[Theory]` class is counted once by class, not once per case (correction 3)
- [ ] Union checker: a renamed class fails as `missing` + `unknown` rather than passing silently
- [ ] Harness: an orphaned child process after a cancelled run is detected and killed — assert no listener remains on the reserved port
- [ ] Harness: a readiness probe that never succeeds fails with a diagnostic naming the port and elapsed time, not a bare timeout
- [ ] Harness: two shards running concurrently never bind the same port (run the port allocator under contention)
- [ ] Suite: `dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj -c Release -m:1` green on Windows and Linux before and after
- [ ] Estate: an appended `docs/ci/baselines/` ledger shows the Windows API mean/P95 against 17.3 min / 35.7 allowance minutes
- [ ] Docs: `node scripts/check-docs-governance.mjs`

## Edge cases

- A `[Theory]` class contributes many TRX rows under one class name — inventory must key on the class, timing history must aggregate.
- A class renamed in the same PR that edits the manifest: the union check must fail before merge, not after.
- Two shards assigned the same expensive host fixture: the collection boundary must be inside one shard or the fixture is built twice.
- Port race between a shard and a leftover process from a cancelled prior run on a self-hosted runner (CI-04) — reservation must be per-run, not per-machine.
- Windows path quoting in the `--filter` expression: class names with generics or nested types break naive quoting.
- TRX missing or truncated because the job was cancelled — timing history must skip, not zero.
- A shard with zero selected tests must be reported as an empty-but-valid run, never as a green with no evidence (the gate's `selected-evidence-missing` code exists for this).

## Reference material

| Kind | Archived path | Good for | Caveats |
| --- | --- | --- | --- |
| Manifest shape | `docs/analysis/2026-08-30-acceleration-bundle/candidates/smart-ci/api-shards.v1.json` | The `{schema_version, shards:[{name, tests[]}]}` shape and the seven behavioural names | **Every class name in it is fictional** and the assembly name is wrong (correction 1) |
| Union checker | `.../candidates/smart-ci/validate_api_shards.py` | Exact-partition algorithm: duplicate/missing/unknown/duplicate-shard-name | Requires an inventory file it cannot produce; does not check that a shard name maps to a real lane or check context |
| Test vector | `.../testing/test-vectors/api-test-inventory.sample.json` | Fixture shape for the checker's own tests | Same fictional names — usable only as a synthetic fixture, never as this repo's inventory |
| Diagram | `.../diagrams/smart-ci-control-loop.svg` | Explaining change → risk → lanes → runner → receipt → report | Explanatory; ADR-0066 §Decision is the contract |
| Blueprint | `.../architecture/SMART_CI_DEPTH_BLUEPRINT.md` | The "fix process overhead before adding parallel jobs" list | See its validation preface |

## Corrections to the bundle

1. **Bundle and the live issue body both name ten slow classes** (`McpHttpAuthTests`,
   `McpHttpIsolationTests`, `McpHttpProposalSurfaceTests`, `McpHttpHostileWriteAuthTests`,
   `ChatSseEndpointTests`, `FirstRunMcpSmokeTests`, `AutomatedJourneyGapTests`, `CoreApiTests`,
   `McpContractTests`, and the assembly `Taskdeck.Api.IntegrationTests`). **True on `main`:**
   `grep -rl` over `backend/tests` finds **none of them** except `MigrationBootstrapTests`. The real
   project is `backend/tests/Taskdeck.Api.Tests` with 176 test classes; the real MCP classes are
   `McpHttpTransportApiKeyTests`, `McpAuthenticationRateLimitingMiddlewareTests`,
   `McpBoardResourcesTests`, `McpToolsTests`, `McpStdioTransportTests`,
   `StandaloneMcpHostFilteringTests`, `McpTelemetryMiddlewareTests` and siblings.
   **Where it came from:** `docs/analysis/2026-08-30-smart-ci/CURRENT_CI_MEASUREMENTS_AS_RECEIVED.json`
   (a snapshot the Smart CI reconciliation explicitly downgraded to "the pack's historical sample")
   — the class names in it were never verified, and `#2330`'s body inherited them verbatim.
   **Consequence:** the shard manifest must be generated from the assembly. Correct `#2330`'s
   Context paragraph before anyone implements against it.
2. **Bundle/issue:** "~6 min Ubuntu vs ~15.5 min Windows (PR #2280 sample)". **True on `main`:** the
   measured 30-run baseline in `docs/ci/CI_BASELINE.md` gives **7.2 vs 17.3 minutes mean**, and the
   cost unit that matters is **35.7 allowance minutes** (Windows x2, per-job round-up). Use the
   baseline, not the single-PR sample; the issue's own "target <=1.5x Ubuntu" must be restated
   against the 2.4x baseline ratio.
3. **Bundle:** "Parameterized tests inventory" appears only as an edge-case bullet. **True:** with
   176 classes and heavy `[Theory]` use, a per-*test* inventory and a per-*class* manifest disagree
   by construction. **Consequence:** fix the unit of the manifest (class) in slice 01 and make the
   checker assert it, or the union check is decorative.
4. **Bundle:** "Trait versus explicit manifest transition" is listed as a decision to receive.
   **True:** ADR-0066 §Decision 6 and the blueprint already rule explicit-manifest-first, traits only
   once they are provably as exact. **Consequence:** not an open decision; delete it from the issue's
   decision list.
5. **Bundle file ownership names** `backend/tests/Taskdeck.Api.IntegrationTests/**`. **True:** that
   path does not exist. **Consequence:** ownership is `backend/tests/Taskdeck.Api.Tests/**`.
6. **Bundle:** "CI-06 depends on #2324" only. **Live issue:** "Depends on CI-01, CI-05." **True:**
   CI-01 `#2325` is **closed**; CI-05 `#2329` is open and its deliverable
   `ci/test-ownership.v1.json` is *documented in `docs/ci/SMART_CI.md` §11 but absent from `ci/`* —
   a live doc/reality drift worth fixing while CI-05 is open. Only slice 04 needs it.
7. **Bundle:** "Weekly/release run the full union" is presented as an acceptance box. **True:** there
   is no weekly sweep yet — CI-10 `#2334` creates it. **Consequence:** either sequence that box
   behind `#2334` or satisfy it with the existing `ci-nightly.yml` until the coordinator exists.
