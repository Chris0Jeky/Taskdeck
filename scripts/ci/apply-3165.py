from pathlib import Path

path = Path('scripts/ci/dev-up.test.mjs')
text = path.read_text(encoding='utf-8')


def replace_once(old: str, new: str) -> None:
    global text
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f'Expected one integration anchor, found {count}: {old[:120]!r}')
    text = text.replace(old, new)


replace_once(
    "import assert from 'node:assert/strict'\n",
    "import assert from 'node:assert/strict'\nimport { AsyncLocalStorage } from 'node:async_hooks'\n",
)
replace_once(
    "  readFile,\n  readdir,\n  rm,\n",
    "  readFile,\n  rm,\n",
)
replace_once(
    "import test from 'node:test'\nimport { fileURLToPath } from 'node:url'\n",
    """import nodeTest from 'node:test'
import { fileURLToPath } from 'node:url'

import {
  DEFAULT_DEV_UP_TEST_TIMEOUT_MS,
  DEFAULT_FIXTURE_DIAGNOSTIC_MARGIN_MS,
  createFixtureCleanupBudget,
  createFixtureLayout,
  describeLiveFixtureProcesses,
  removeFixtureDirectory,
} from './dev-up-fixture-cleanup.mjs'
""",
)
replace_once(
    "const RESET_CYCLE_TEARDOWN_TIMEOUT_MS = 45_000\n",
    """const RESET_CYCLE_TEARDOWN_TIMEOUT_MS = 45_000

function configuredDefaultTestTimeoutMs(argv = process.execArgv) {
  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index]
    const inlineMatch = /^--test-timeout=(\\d+)$/.exec(argument)
    const rawValue = inlineMatch?.[1] ?? (argument === '--test-timeout' ? argv[index + 1] : null)
    if (rawValue == null) continue
    const timeoutMs = Number(rawValue)
    if (Number.isFinite(timeoutMs) && timeoutMs > DEFAULT_FIXTURE_DIAGNOSTIC_MARGIN_MS) {
      return timeoutMs
    }
  }
  return DEFAULT_DEV_UP_TEST_TIMEOUT_MS
}

const defaultTestTimeoutMs = configuredDefaultTestTimeoutMs()
const testBudgetStorage = new AsyncLocalStorage()

function test(name, optionsOrCallback, maybeCallback) {
  const options = typeof optionsOrCallback === 'function' ? {} : (optionsOrCallback ?? {})
  const callback = typeof optionsOrCallback === 'function' ? optionsOrCallback : maybeCallback
  if (typeof callback !== 'function') return nodeTest(name, options)

  return nodeTest(name, options, (context) => {
    const cleanupBudget = createFixtureCleanupBudget({
      testStartedAtMs: Date.now(),
      testTimeoutMs: Number(options.timeout ?? defaultTestTimeoutMs),
    })
    return testBudgetStorage.run(cleanupBudget, () => callback(context))
  })
}
""",
)

cleanup_start_marker = '// Windows refuses to remove a directory while any live process still uses it as a working\n'
cleanup_end_marker = '\nasync function createFixture(platform) {'
cleanup_start = text.find(cleanup_start_marker)
cleanup_end = text.find(cleanup_end_marker, cleanup_start)
if cleanup_start < 0 or cleanup_end < 0:
    raise RuntimeError('Could not locate the fixture-cleanup implementation block')

replacement_cleanup = """// Standalone seam fixtures are small and do not own launcher processes. Keep their cleanup
// bounded by Node's native retry count; full launcher fixtures use the absolute per-test budget
// captured by removeFixtureDirectory below.
async function removeDirectory(root) {
  await rm(root, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 })
}

async function removeFixture(fixture) {
  let teardownFailure = null
  try {
    await removeFixtureDirectory(fixture)
  } catch (error) {
    teardownFailure = error
  }

  try {
    await rm(fixture.envelopeRoot, { recursive: true, force: true })
  } catch (envelopeError) {
    const liveProcesses = await describeLiveFixtureProcesses(fixture)
    if (!teardownFailure) {
      throw new Error(
        `DEV_UP_FIXTURE_TEARDOWN_FAILED: fixture evidence envelope ${fixture.envelopeRoot} ` +
          `could not be removed. Fixture processes still alive: ${liveProcesses}.`,
        { cause: envelopeError },
      )
    }
    teardownFailure.message +=
      ` Evidence envelope cleanup also failed (${envelopeError?.code ?? envelopeError}); ` +
      `fixture processes still alive: ${liveProcesses}.`
  }

  if (teardownFailure) throw teardownFailure
}
"""
text = text[:cleanup_start] + replacement_cleanup + text[cleanup_end:]

replace_once(
    """async function createFixture(platform) {
  const root = await mkdtemp(join(tmpdir(), `taskdeck-dev-up-${platform.name.toLowerCase()}-`))
  const scriptsDir = join(root, 'scripts')
""",
    """async function createFixture(platform) {
  const cleanupBudget = testBudgetStorage.getStore()
  assert.ok(cleanupBudget, 'createFixture must run inside the budgeted node:test wrapper')
  const envelopeRoot = await mkdtemp(
    join(tmpdir(), `taskdeck-dev-up-${platform.name.toLowerCase()}-`),
  )
  const { root, livePidFile } = createFixtureLayout(envelopeRoot)
  await mkdir(root, { recursive: true })
  const scriptsDir = join(root, 'scripts')
""",
)
replace_once(
    """  return {
    root,
    scriptsDir,
""",
    """  return {
    envelopeRoot,
    cleanupBudget,
    root,
    scriptsDir,
""",
)
replace_once(
    """    npmLog: join(root, 'events.jsonl'),
    npmReleaseFile: join(root, '.release-npm-ci'),
    livePidFile: join(root, 'live-pids.log'),
""",
    """    npmLog: join(root, 'events.jsonl'),
    npmReleaseFile: join(root, '.release-npm-ci'),
    livePidFile,
""",
)

required_fragments = [
    "const testBudgetStorage = new AsyncLocalStorage()",
    "testTimeoutMs: Number(options.timeout ?? defaultTestTimeoutMs)",
    "await removeFixtureDirectory(fixture)",
    "const { root, livePidFile } = createFixtureLayout(envelopeRoot)",
    "cleanupBudget,",
    "livePidFile,",
]
for fragment in required_fragments:
    if fragment not in text:
        raise RuntimeError(f'Post-patch assertion missing {fragment!r}')
if "livePidFile: join(root, 'live-pids.log')" in text:
    raise RuntimeError('PID ledger still lives inside the recursively removed workspace')
if 'const FIXTURE_REMOVAL_TIMEOUT_MS = 30_000' in text:
    raise RuntimeError('Independent fixture-removal deadline survived the integration patch')

path.write_text(text, encoding='utf-8')
