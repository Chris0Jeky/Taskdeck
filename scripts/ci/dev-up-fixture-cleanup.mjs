import { readFile, readdir, rm } from 'node:fs/promises'
import { join } from 'node:path'

export const DEFAULT_DEV_UP_TEST_TIMEOUT_MS = 30_000
export const DEFAULT_FIXTURE_DIAGNOSTIC_MARGIN_MS = 2_000
const DEFAULT_REMOVAL_DELAY_MS = 50
const DEFAULT_MAX_REMOVAL_DELAY_MS = 500
const DEFAULT_RETRY_CODES = new Set(['EBUSY', 'EPERM', 'ENOTEMPTY', 'EMFILE', 'ENFILE'])

function requireFiniteNonNegative(value, name) {
  if (!Number.isFinite(value) || value < 0) {
    throw new TypeError(`${name} must be a finite non-negative number; received ${value}`)
  }
}

export function createFixtureCleanupBudget({
  testStartedAtMs,
  testTimeoutMs = DEFAULT_DEV_UP_TEST_TIMEOUT_MS,
  diagnosticMarginMs = DEFAULT_FIXTURE_DIAGNOSTIC_MARGIN_MS,
} = {}) {
  requireFiniteNonNegative(testStartedAtMs, 'testStartedAtMs')
  requireFiniteNonNegative(testTimeoutMs, 'testTimeoutMs')
  requireFiniteNonNegative(diagnosticMarginMs, 'diagnosticMarginMs')
  if (testTimeoutMs <= diagnosticMarginMs) {
    throw new RangeError(
      `testTimeoutMs (${testTimeoutMs}) must exceed diagnosticMarginMs (${diagnosticMarginMs})`,
    )
  }

  return Object.freeze({
    testStartedAtMs,
    testTimeoutMs,
    diagnosticMarginMs,
    cleanupDeadlineMs: testStartedAtMs + testTimeoutMs - diagnosticMarginMs,
    testDeadlineMs: testStartedAtMs + testTimeoutMs,
  })
}

export function createFixtureLayout(envelopeRoot) {
  if (typeof envelopeRoot !== 'string' || envelopeRoot.length === 0) {
    throw new TypeError('envelopeRoot must be a non-empty path')
  }

  return Object.freeze({
    envelopeRoot,
    root: join(envelopeRoot, 'workspace'),
    livePidFile: join(envelopeRoot, 'live-pids.log'),
  })
}

async function readOptional(path) {
  try {
    return await readFile(path, 'utf8')
  } catch (error) {
    if (error?.code === 'ENOENT') return null
    throw error
  }
}

function defaultIsProcessAlive(pid) {
  try {
    process.kill(pid, 0)
    return true
  } catch (error) {
    if (error?.code === 'ESRCH') return false
    if (error?.code === 'EPERM') return true
    throw error
  }
}

export async function describeLiveFixtureProcesses(
  fixture,
  { readText = readOptional, isProcessAlive = defaultIsProcessAlive } = {},
) {
  if (!fixture?.livePidFile) return 'not recorded'
  const text = await readText(fixture.livePidFile)
  if (!text) return 'none recorded'

  const seen = new Set()
  const alive = []
  for (const line of text.split(/\r?\n/)) {
    const [kind, rawPid] = line.trim().split(/\s+/)
    const pid = Number(rawPid)
    if (!Number.isSafeInteger(pid) || pid <= 0 || seen.has(pid)) continue
    seen.add(pid)
    if (isProcessAlive(pid)) alive.push(`${kind} pid ${pid}`)
  }
  return alive.length > 0 ? alive.join(', ') : 'none still alive'
}

async function describeRemainingEntries(root, { listEntries = readdir } = {}) {
  try {
    const entries = await listEntries(root)
    return entries.length > 0 ? entries.join(', ') : 'none (only the root itself is held)'
  } catch (error) {
    if (error?.code === 'ENOENT') return 'none (root already gone)'
    return `unreadable (${error?.code ?? error})`
  }
}

async function createTeardownFailure(fixture, error, dependencies) {
  const { cleanupBudget, root } = fixture
  const nowMs = dependencies.now()
  const liveProcesses = await describeLiveFixtureProcesses(fixture, dependencies)
  const remainingEntries = await describeRemainingEntries(root, dependencies)
  const elapsedMs = Math.max(0, nowMs - cleanupBudget.testStartedAtMs)
  const operation = [error?.code, error?.syscall, error?.path]
    .filter((value) => value != null && value !== '')
    .join(' on ')

  return new Error(
    `DEV_UP_FIXTURE_TEARDOWN_FAILED: ${root} was still in use when its cleanup deadline ` +
      `elapsed ${elapsedMs}ms into a ${cleanupBudget.testTimeoutMs}ms test; ` +
      `${cleanupBudget.diagnosticMarginMs}ms was reserved for teardown diagnostics. ` +
      `${operation || 'The recursive removal failed without operation metadata'}. ` +
      `Fixture processes still alive: ${liveProcesses}. ` +
      `Remaining entries: ${remainingEntries}. ` +
      'Something is still using the workspace as a working directory or holding one of its entries.',
    { cause: error },
  )
}

export async function removeFixtureDirectory(fixture, options = {}) {
  if (!fixture?.root || !fixture?.cleanupBudget) {
    throw new TypeError('fixture.root and fixture.cleanupBudget are required')
  }

  const dependencies = {
    now: options.now ?? Date.now,
    remove:
      options.remove ??
      ((path) => rm(path, { recursive: true, force: true })),
    sleep:
      options.sleep ??
      ((delayMs) => new Promise((resolve) => setTimeout(resolve, delayMs))),
    readText: options.readText ?? readOptional,
    listEntries: options.listEntries ?? readdir,
    isProcessAlive: options.isProcessAlive ?? defaultIsProcessAlive,
  }
  const retryCodes = options.retryCodes ?? DEFAULT_RETRY_CODES
  const maxDelayMs = options.maxDelayMs ?? DEFAULT_MAX_REMOVAL_DELAY_MS
  let delayMs = options.initialDelayMs ?? DEFAULT_REMOVAL_DELAY_MS
  requireFiniteNonNegative(maxDelayMs, 'maxDelayMs')
  requireFiniteNonNegative(delayMs, 'initialDelayMs')

  for (;;) {
    try {
      await dependencies.remove(fixture.root)
      return
    } catch (error) {
      if (!retryCodes.has(error?.code)) throw error

      const remainingMs = fixture.cleanupBudget.cleanupDeadlineMs - dependencies.now()
      if (remainingMs <= 0) {
        throw await createTeardownFailure(fixture, error, dependencies)
      }

      const boundedDelayMs = Math.min(delayMs, remainingMs)
      await dependencies.sleep(boundedDelayMs)
      delayMs = Math.min(Math.max(delayMs * 2, 1), maxDelayMs)
    }
  }
}

export async function removeFixture(fixture, options = {}) {
  if (!fixture?.envelopeRoot) {
    throw new TypeError('fixture.envelopeRoot is required')
  }

  // Do not attempt a second recursive traversal when workspace cleanup has already exhausted the
  // per-test budget. Let that named failure escape immediately so the reserved diagnostic margin
  // remains available to node:test and the evidence envelope stays intact for inspection.
  await removeFixtureDirectory(fixture, options)

  const removeEnvelope =
    options.removeEnvelope ?? ((path) => rm(path, { recursive: true, force: true }))
  try {
    await removeEnvelope(fixture.envelopeRoot)
  } catch (error) {
    const liveProcesses = await describeLiveFixtureProcesses(fixture, options)
    throw new Error(
      `DEV_UP_FIXTURE_TEARDOWN_FAILED: fixture evidence envelope ${fixture.envelopeRoot} ` +
        `could not be removed. Fixture processes still alive: ${liveProcesses}.`,
      { cause: error },
    )
  }
}
