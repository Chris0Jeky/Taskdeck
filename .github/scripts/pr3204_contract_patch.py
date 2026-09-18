from pathlib import Path


def replace_exact(path: str, old: str, new: str) -> None:
    target = Path(path)
    text = target.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{path}: expected one anchor, found {count}")
    target.write_text(text.replace(old, new), encoding="utf-8")


suite = "frontend/taskdeck-web/tests/run-vite-dev.spec.ts"
lazy_test = """  it('withholds the marker when a literal lazy route has a broken nested import', async () => {
    const fixtureRoot = await createFixture({
      'src/main.ts': "export const loadLazyRoute = () => import('./lazy-route.ts')\\n",
      'src/lazy-route.ts': "import 'taskdeck-missing-lazy-route-dependency'\\n",
    })
    const port = await findAvailablePort()
    const logs: string[] = []

    await expect(
      runViteDev({
        args: [fixtureRoot, '--host', '127.0.0.1', '--port', String(port)],
        env: {},
        logger: captureLogger(logs),
      }),
    ).rejects.toThrow(/taskdeck-missing-lazy-route-dependency/)

    expect(logs.some((line) => line.startsWith('TASKDECK_DEV_FRONTEND_READY '))).toBe(false)
    expect(await canBindPort(port)).toBe(true)
    await expect(pathExists(path.join(fixtureRoot, 'dist'))).resolves.toBe(false)
  }, 20_000)

"""
replace_exact(
    suite,
    "  it('loads the root Vite config and retains proxy settings in a healthy graph', async () => {\n",
    lazy_test + "  it('loads the root Vite config and retains proxy settings in a healthy graph', async () => {\n",
)
Path("frontend/taskdeck-web/tests/run-vite-dev-lazy-route.spec.ts").unlink()

replace_exact(
    "frontend/taskdeck-web/scripts/check-dev-entry-graph.mjs",
    """/**
 * Ask Vite to resolve and transform every literal import reachable from the
 * Taskdeck entry module. This exercises the development resolver/plugin graph
 * without a browser and without writing a production bundle.
 */
""",
    """/**
 * Ask Vite to resolve and transform every literal static and literal dynamic
 * import reachable from the Taskdeck entry module. Vite 8.3 exposes both kinds
 * through ModuleNode.staticImportedUrls. Computed runtime imports cannot be
 * enumerated by this readiness check.
 *
 * This exercises the development resolver/plugin graph without a browser and
 * without writing a production bundle.
 */
""",
)
replace_exact(
    "frontend/taskdeck-web/scripts/check-dev-entry-graph.mjs",
    """      // Vite keeps literal import URLs separate from plugin-added watch files.
      // Traversing importedModules directly would incorrectly execute Tailwind
      // content dependencies (including Markdown and test fixtures) as modules.
""",
    """      // Vite 8.3 puts resolved literal static and literal dynamic imports in
      // staticImportedUrls. Plugin-added watch files remain outside that set;
      // traversing importedModules would incorrectly execute Tailwind content
      // dependencies (including Markdown and test fixtures) as modules.
      // Computed runtime imports have no enumerable URL and stay outside this marker.
""",
)

status_section = """## Source-launcher literal-import readiness (#1900 candidate)

The source Vite launcher now has a real-provider regression for router-style literal lazy imports.
A missing dependency behind `import('./lazy-route')` must fail startup before
`TASKDECK_DEV_FRONTEND_READY`, close the Vite listener and write no production bundle. Under the
pinned Vite 8.3 contract, `ModuleNode.staticImportedUrls` contains resolved static top-level imports
and literal dynamic imports while excluding plugin watch files, so the existing traversal already
covers those lazy routes. Computed runtime imports are not enumerable and remain outside the marker;
production build, typecheck and route tests are separate evidence. Marker schema version 1 and both
launcher consumers are unchanged. Exact-head hosted qualification remains required before delivery.

"""
replace_exact(
    "docs/STATUS.md",
    "Last Updated: 2026-09-12\n\n",
    "Last Updated: 2026-09-18\n\n" + status_section,
)

plan_section = """## Source-launcher lazy-route readiness contract (#1900 candidate)

Retain the current Vite graph traversal rather than adding a second parser. Vite 8.3 exposes both
resolved static top-level imports and literal dynamic imports through `staticImportedUrls`; the
real-Vite regression must keep a broken nested dependency behind a literal lazy route from producing
the readiness marker. Keep computed runtime imports and plugin-added watch files explicitly outside
this marker, with production build/typecheck/route tests owning those wider surfaces. Preserve marker
schema version 1, exact URL/port validation and existing launcher failure/cleanup behavior. Complete
Windows and Ubuntu hosted qualification and a fresh review before closing #1900.

"""
replace_exact(
    "docs/IMPLEMENTATION_MASTERPLAN.md",
    "Last Updated: 2026-09-12\n\n",
    "Last Updated: 2026-09-18\n\n" + plan_section,
)

guide_anchor = """Companion Active Docs:
- `docs/STATUS.md`
- `docs/IMPLEMENTATION_MASTERPLAN.md`
- `docs/TESTING_GUIDE.md`
- `docs/MANUAL_TEST_CHECKLIST.md`
- `docs/GOLDEN_PRINCIPLES.md`

"""
guide_section = """Companion Active Docs:
- `docs/STATUS.md`
- `docs/IMPLEMENTATION_MASTERPLAN.md`
- `docs/TESTING_GUIDE.md`
- `docs/MANUAL_TEST_CHECKLIST.md`
- `docs/GOLDEN_PRINCIPLES.md`

## Source-launcher literal lazy routes (#1900)

Run the real-provider readiness seam from `frontend/taskdeck-web`:

```powershell
npx vitest run tests/run-vite-dev.spec.ts --maxWorkers=1
```

The suite proves nested static imports and router-style literal `import()` dependencies are
transformed before `TASKDECK_DEV_FRONTEND_READY`; a missing dependency withholds the marker, closes
the listener and writes no bundle. This relies on the pinned Vite 8.3 contract that
`staticImportedUrls` contains both resolved static top-level and literal dynamic imports. It does
not prove computed runtime imports, arbitrary user-driven import specifiers or plugin watch files.
Keep typecheck, production build and route/component tests as separate gates. The marker remains
schema version 1 and the Bash/PowerShell launchers retain their exact URL/port validation.

"""
replace_exact("docs/TESTING_GUIDE.md", guide_anchor, guide_section)
replace_exact(
    "docs/TESTING_GUIDE.md",
    "Last Updated: 2026-09-10\n",
    "Last Updated: 2026-09-18\n",
)
