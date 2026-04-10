# Mutation Testing Policy

Last Updated: 2026-04-09

## Purpose

Mutation testing measures how well our test suite detects code changes by introducing small, systematic mutations (e.g., flipping conditionals, removing statements, changing operators) into production code and checking whether existing tests catch them. A mutant that survives indicates a gap in test assertion quality -- not necessarily missing tests, but potentially weak or missing assertions in existing tests.

This is a **quality signal**, not a gatekeeping mechanism. Mutation testing complements line/branch coverage by revealing assertion blind spots that coverage metrics cannot detect.

## Current Scope

### Backend (Stryker.NET)

- **Target**: `Taskdeck.Domain` project
- **Test project**: `Taskdeck.Domain.Tests`
- **Rationale**: Domain contains core business logic (entity state machines, validation rules, invariants) where surviving mutants have the highest impact. Domain is pure C# with no infrastructure dependencies, making mutation runs fast and deterministic.
- **Config**: `backend/stryker-config.json`

### Frontend (Stryker JS/TS)

- **Target**: `src/store/captureStore.ts`, `src/store/boardStore.ts`, and `src/store/board/*.ts` (board store submodules)
- **Test runner**: Vitest
- **Rationale**: These two Pinia stores are the core data flow layer for the capture-to-board pipeline. Mutations here have direct product impact on the golden path.
- **Config**: `frontend/taskdeck-web/stryker.config.mjs`

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
# Install Stryker.NET as a global tool (once)
dotnet tool install --global dotnet-stryker

# Run from the backend/ directory
cd backend
dotnet stryker --config-file stryker-config.json
```

Report: `backend/StrykerOutput/<timestamp>/reports/mutation-report.html`

### Frontend (local)

```bash
cd frontend/taskdeck-web
npm run mutation:test
```

Report: `frontend/taskdeck-web/reports/mutation/mutation.html`

### CI

The mutation testing workflow runs:
- **Weekly**: Sunday 04:00 UTC (automatic)
- **On demand**: via `workflow_dispatch` from the Actions tab

Reports are uploaded as GitHub Actions artifacts with 30-day retention.

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

1. **File an issue** with the label `test-hardening` and link to the mutation report artifact
2. **Categorize** surviving mutants by triage priority (see above)
3. **Bundle fixes**: Group related assertion improvements into a single PR per module rather than one PR per mutant
4. **Do not chase 100%**: Some surviving mutants are acceptable (e.g., log messages, cosmetic formatting). Document intentional exclusions:
   - **Backend (Stryker.NET)**: Use `excluded-mutations` or `ignored-methods` in `backend/stryker-config.json`
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
