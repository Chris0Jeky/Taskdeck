# Mutation Testing Policy

Last Updated: 2026-09-12

## Purpose

Mutation testing measures how well our test suite detects code changes by introducing small, systematic mutations (e.g., flipping conditionals, removing statements, changing operators) into production code and checking whether existing tests catch them. A mutant that survives indicates a gap in test assertion quality -- not necessarily missing tests, but potentially weak or missing assertions in existing tests.

This is a **quality signal**, not a gatekeeping mechanism. Mutation testing complements line/branch coverage by revealing assertion blind spots that coverage metrics cannot detect.

## Current Scope

### Backend (Stryker.NET)

- **Target**: `Taskdeck.Domain` project
- **Test project**: `Taskdeck.Domain.Tests`
- **Tool contract**: Stryker.NET `4.16.0`
- **Rationale**: Domain contains core business logic (entity state machines, validation rules, invariants) where surviving mutants have the highest impact. Domain is pure C# with no infrastructure dependencies, making it the narrowest deterministic backend target; its full mutation set is still a long-running workload.
- **Config**: `backend/stryker-config.json`
- **Execution context**: run from `backend/tests/Taskdeck.Domain.Tests`; do not add `solution` or `test-projects` to the config because solution context takes precedence and discovers unrelated tests
- **Preflight**: `scripts/ci/Test-StrykerConfig.ps1 -SelfTest` rejects obsolete schema keys, solution-context selectors, and workflow/artifact drift before the long mutation run starts

### Frontend (Stryker JS/TS)

- **Target**: `src/store/captureStore.ts`, `src/store/boardStore.ts`, and `src/store/board/*.ts` (board store submodules)
- **Test runner**: Vitest
- **Rationale**: These two Pinia stores are the core data flow layer for the capture-to-board pipeline. Mutations here have direct product impact on the golden path.
- **Config**: `frontend/taskdeck-web/stryker.config.mjs`
- **Activation smoke test**: `npm run mutation:smoke` runs four board-list deletion mutants against the focused CRUD suite with a 100% break threshold. Keep the Stryker/Vitest pair compatible; a local comparison with this repository's Stryker 10 setup produced zero per-mutant executions with Vitest 5.0.0, while the pinned Vitest 4.1.x line killed all four mutants.

## Threshold Strategy

| Metric | Current Setting | Meaning |
|--------|----------------|---------|
| `high` | 80% | Score above this is considered strong |
| `low` | 60% | Score below this triggers investigation |
| `break` | 0% | No build-breaking threshold (non-blocking lane) |

### Why these numbers

- **60% low threshold**: Realistic starting point given the existing test suite was not written with mutation testing in mind. Many surviving mutants will be in areas with adequate line coverage but weak assertions.
- **80% high threshold**: Aspirational target. Reaching this indicates the test suite actively verifies behavior rather than just exercising code paths.
- **0% break threshold**: Mutation testing is a triage signal. Breaking builds on mutation score before the team has calibrated expectations would create noise, not value.

### Threshold evolution

After the first 3-4 runs:
1. Review the baseline mutation scores
2. Set `break` to a value 5-10 points below the observed baseline (prevents regression without requiring immediate improvement)
3. Ratchet `low` upward as test hardening PRs land
4. Consider expanding scope (add `Taskdeck.Application` on backend, add more stores on frontend) once the initial modules stabilize above 70%

## Running Mutation Tests

### Backend (local)

```bash
# From the repository root, validate the checked-in schema contract.
# Native Windows PowerShell:
powershell -NoProfile -File scripts/ci/Test-StrykerConfig.ps1 -SelfTest
# PowerShell 7 on Linux/macOS/Windows uses the equivalent `pwsh -File ...` form.

# Restore the repository-local Stryker.NET 4.16.0 tool manifest. This remains
# deterministic even when another Stryker version is installed globally.
dotnet tool restore

# Run from the Domain test project so Stryker discovers only that test project.
cd backend/tests/Taskdeck.Domain.Tests
dotnet tool run dotnet-stryker -- --config-file ../../stryker-config.json --output ../../StrykerOutput
```

Report: `backend/StrykerOutput/<timestamp>/reports/mutation-report.html`

### Frontend (local)

```bash
cd frontend/taskdeck-web
npm run mutation:smoke
npm run mutation:test
```

Report: `frontend/taskdeck-web/reports/mutation/mutation.html`

#### Reproducing the Vitest dry run without a full mutation run

Stryker runs the whole Vitest suite once as a **dry run** before it executes any mutant. If that dry
run fails, the lane produces no report at all, whatever the mutation score would have been. The dry
run does not use the repository's default Vitest pool: `@stryker-mutator/vitest-runner` (v10,
`#getVitestPoolConfig`) forces `pool: 'threads', maxWorkers: 1`, overriding the `forks` pool the
ordinary unit jobs use.

Reproduce that exact shape in seconds, without waiting for a mutation run:

```bash
cd frontend/taskdeck-web
# Whole suite in Stryker's pool shape (~7 min on a dev box):
npx vitest --run --pool=threads --maxWorkers=1 --maxConcurrency=1
# One spec, seconds:
npx vitest --run --pool=threads --maxWorkers=1 src/tests/utils/timeZone.spec.ts
```

Deliberately not an npm script: `frontend/taskdeck-web/package.json` is a declared control path
(`ci/policy.v1.json`), so adding one would make an otherwise ordinary test change an R4 PR.

**Known pool-dependent trap — timezone stubs (#2943).** `vi.stubEnv('TZ', zone)` changes the runtime
zone only as a side effect of Node's real environment store notifying V8. That notification does not
happen under `pool: 'threads'`: `process.env.TZ` reads back as the requested zone while `Date` and
`Intl` keep the host zone. A spec that stubs `TZ` and then asserts on a zone-derived value therefore
measures the CI runner's zone during the dry run — which is how
[run 34518952589](https://github.com/Chris0Jeky/Taskdeck/actions/runs/34518952589) failed on the
PaperHomeView day-boundary rows (`expected -1, received 0`) while the ordinary frontend unit jobs
passed on both Ubuntu and Windows.

Use `frontend/taskdeck-web/src/tests/utils/timeZone.ts` instead of `vi.stubEnv('TZ', …)` for any
assertion whose value depends on the zone. It derives everything from explicit
`Intl.DateTimeFormat(…, { timeZone })` arguments, so it behaves identically in both pools and on a
host in any zone.

### CI

The mutation testing workflow is manual-only via `workflow_dispatch` from the Actions tab. The frontend job runs the activation smoke test before the non-blocking full mutation report, so an incompatible test-runner upgrade fails early instead of producing an apparently valid zero-execution report.

Reports are uploaded as GitHub Actions artifacts with 30-day retention.
The backend job has a finite 180-minute ceiling for the full Domain mutation set, and artifact upload fails when no report was produced.

The first repaired backend-only baseline, [run 30236307062](https://github.com/Chris0Jeky/Taskdeck/actions/runs/30236307062) on exact workflow head `307add004fbe142321a6ec11be21fab708824d5d`, completed in 192 seconds. It created 3,682 mutants: 2,351 killed, 576 survived, 2 timed out, and 753 skipped, for a 70.75% score. The non-empty two-file report artifact is 874,386 bytes (SHA-256 `0e8a9a41b8cd484b6c267bd914c57cda0ffa973f59d8989e89038157605f21c8`). Keep the 0% break threshold until the policy's 3-4-run calibration window exists.

## Interpreting Reports

### Mutant statuses

| Status | Meaning | Action |
|--------|---------|--------|
| **Killed** | Test suite detected the mutation | No action needed |
| **Survived** | No test failed when this mutation was applied | Investigate -- may need a stronger assertion or new test case |
| **No coverage** | No test executes the mutated code | Indicates a coverage gap; add test coverage first |
| **Timeout** | Tests timed out with the mutation applied | Usually counts as "detected"; may indicate slow tests |
| **Compile error** | Mutation caused a compile error | Automatically excluded; not actionable |

### Triage priority

1. **Survived mutants in conditional logic** (if/else, switch, guards): Highest priority. These often indicate missing boundary tests or assertion gaps on error paths.
2. **Survived mutants in arithmetic/comparison operators**: Medium priority. May indicate tests check existence but not correctness of computed values.
3. **Survived mutants in string literals or log messages**: Low priority. Often acceptable -- tests should not typically assert on log text.
4. **Survived mutants in constructor defaults**: Low priority unless the default affects business behavior.

## Follow-up Process

When mutation testing reveals surviving mutants:

1. **File an issue** with the existing `testing` and `hardening` labels and link to the mutation report artifact
2. **Categorize** surviving mutants by triage priority (see above)
3. **Bundle fixes**: Group related assertion improvements into a single PR per module rather than one PR per mutant
4. **Do not chase 100%**: Some surviving mutants are acceptable (e.g., log messages, cosmetic formatting). Document intentional exclusions:
   - **Backend (Stryker.NET 4.16.0)**: Add Stryker patterns as non-empty string entries in the `ignore-mutations` or `ignore-methods` arrays in `backend/stryker-config.json`; the preflight accepts empty arrays and non-empty string entries while rejecting scalar/invalid entries and the obsolete `excluded-mutations` and `ignored-methods` spellings
   - **Frontend (Stryker JS)**: Adjust `mutate` glob patterns in `stryker.config.mjs` or use inline `// Stryker disable` comments in source files

## Scope Expansion Roadmap

Phase 1 (current): `Taskdeck.Domain` + `captureStore` / `boardStore`
Phase 2 (planned): Add `Taskdeck.Application` service layer (use-case orchestration, proposal lifecycle)
Phase 3 (future): Add more frontend stores (`sessionStore`, `queueStore`) and critical composables
Phase 4 (aspirational): Infrastructure layer (repository query correctness), API controller input validation

Each phase expansion should be accompanied by a threshold recalibration based on observed scores.

## Excluded from Mutation Testing

The following are intentionally excluded and should remain so:

- **Infrastructure layer** (EF Core migrations, DbContext configuration): Mutations here are almost always compile errors or require a real database
- **API startup/DI wiring** (`Program.cs`, service registration): Not meaningful mutation targets
- **Test projects themselves**: Mutating tests is circular
- **Generated code** (`obj/`, `bin/`, node_modules): Not production code

## References

- [Stryker.NET documentation](https://stryker-mutator.io/docs/stryker-net/introduction/)
- [Stryker JS documentation](https://stryker-mutator.io/docs/stryker-js/introduction/)
- [Mutation testing theory](https://stryker-mutator.io/docs/General/mutation-testing/)
- Issue: #90
