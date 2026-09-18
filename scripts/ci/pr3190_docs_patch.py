from pathlib import Path

path = Path("docs/testing/MUTATION_TESTING_POLICY.md")
source = path.read_text(encoding="utf-8")

marker = "- **Config**: `frontend/taskdeck-web/stryker.config.mjs`\n"
replacement = marker + (
    "- **Source-text guard boundary**: specs that parse raw production source remain mandatory in ordinary "
    "Vitest/required CI, but a guard that reads a current `mutate` target must be registered in "
    "`sourceTextGuardTests` beside the mutate list. Stryker excludes only those registered specs through "
    "`testFiles`, preventing instrumented sandbox text from invalidating a static repository-shape assertion. "
    "`scripts/stryker-source-text-guards.test.mjs` pins the registry/exclusion contract.\n"
)
if source.count(marker) != 1:
    raise SystemExit("Could not locate the frontend Stryker config policy marker exactly once")
source = source.replace(marker, replacement, 1)

old = """Reproduce that exact shape in seconds, without waiting for a mutation run:

```bash
cd frontend/taskdeck-web
# Whole suite in Stryker's pool shape (~7 min on a dev box):
npx vitest --run --pool=threads --maxWorkers=1 --maxConcurrency=1
# One spec, seconds:
npx vitest --run --pool=threads --maxWorkers=1 src/tests/utils/timeZone.spec.ts
```
"""
new = """Reproduce the runner pool shape or Stryker's complete initial selection without executing mutants:

```bash
cd frontend/taskdeck-web
# Whole ordinary suite in Stryker's pool shape (~7 min on a dev box):
npx vitest --run --pool=threads --maxWorkers=1 --maxConcurrency=1
# One spec, seconds:
npx vitest --run --pool=threads --maxWorkers=1 src/tests/utils/timeZone.spec.ts
# Exact Stryker sandbox/instrumentation dry run, including mutation-only test selection:
npx stryker run --dryRunOnly
```

The direct Vitest commands intentionally keep source-text guards enabled. Only the final Stryker command applies
`stryker.config.mjs`'s mutation-only `testFiles` exclusions. This separation proves that static repository-shape
guards remain load-bearing in ordinary CI while instrumented source is tested only by behavioral specs.
"""
if source.count(old) != 1:
    raise SystemExit("Could not locate the existing frontend dry-run command block exactly once")
source = source.replace(old, new, 1)
path.write_text(source, encoding="utf-8", newline="\n")

Path(".github/workflows/pr-3190-dry-run-proof.yml").unlink()
Path("scripts/ci/pr3190_docs_patch.py").unlink()
