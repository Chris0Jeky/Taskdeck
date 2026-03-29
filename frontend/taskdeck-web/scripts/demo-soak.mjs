/**
 * demo-soak.mjs
 *
 * Long-run soak mode that runs director scenarios in a loop with configurable
 * duration or iteration count. Tracks cumulative metrics and outputs a summary.
 */

/**
 * @typedef {object} SoakConfig
 * @property {number} [maxIterations] - Max number of runs (0 = unlimited, use maxDurationMs)
 * @property {number} [maxDurationMs] - Max total duration in ms (0 = unlimited, use maxIterations)
 * @property {number} [cooldownMs] - Pause between runs in ms
 */

/**
 * @typedef {object} SoakIterationResult
 * @property {number} iteration
 * @property {'pass' | 'fail'} status
 * @property {number} durationMs
 * @property {number} eventCount
 * @property {string | null} error
 */

/**
 * @typedef {object} SoakSummary
 * @property {string} startedAt
 * @property {string} endedAt
 * @property {number} totalRuns
 * @property {number} passCount
 * @property {number} failCount
 * @property {number} passRate - Percentage 0-100
 * @property {number} totalDurationMs
 * @property {number} avgIterationMs
 * @property {number} minIterationMs
 * @property {number} maxIterationMs
 * @property {number} timingDriftMs - Difference between fastest and slowest run
 * @property {SoakIterationResult[]} iterations
 * @property {object} memoryIndicators
 */

/**
 * Default soak configuration.
 * @returns {SoakConfig}
 */
export function defaultSoakConfig() {
  return {
    maxIterations: 10,
    maxDurationMs: 0,
    cooldownMs: 500,
  }
}

/**
 * Validates soak configuration.
 * @param {SoakConfig} config
 * @returns {SoakConfig}
 */
export function validateSoakConfig(config) {
  const maxIterations = Number(config?.maxIterations ?? 10)
  const maxDurationMs = Number(config?.maxDurationMs ?? 0)
  const cooldownMs = Number(config?.cooldownMs ?? 500)

  if (maxIterations <= 0 && maxDurationMs <= 0) {
    throw new Error(
      'Soak config requires at least one of maxIterations > 0 or maxDurationMs > 0',
    )
  }

  if (maxIterations < 0 || !Number.isFinite(maxIterations)) {
    throw new Error(`Invalid maxIterations: ${maxIterations}`)
  }

  if (maxDurationMs < 0 || !Number.isFinite(maxDurationMs)) {
    throw new Error(`Invalid maxDurationMs: ${maxDurationMs}`)
  }

  if (cooldownMs < 0 || !Number.isFinite(cooldownMs)) {
    throw new Error(`Invalid cooldownMs: ${cooldownMs}`)
  }

  return { maxIterations, maxDurationMs, cooldownMs }
}

/**
 * Determines whether the soak loop should continue.
 * @param {number} iteration - Current 0-based iteration index
 * @param {number} elapsedMs - Time elapsed since soak start
 * @param {SoakConfig} config
 * @returns {boolean}
 */
export function shouldContinueSoak(iteration, elapsedMs, config) {
  if (config.maxIterations > 0 && iteration >= config.maxIterations) {
    return false
  }

  if (config.maxDurationMs > 0 && elapsedMs >= config.maxDurationMs) {
    return false
  }

  return true
}

/**
 * Collects memory indicators from the Node.js process (if available).
 * @returns {object}
 */
export function collectMemoryIndicators() {
  if (typeof process !== 'undefined' && typeof process.memoryUsage === 'function') {
    const mem = process.memoryUsage()
    return {
      heapUsedMB: Math.round((mem.heapUsed / 1024 / 1024) * 100) / 100,
      heapTotalMB: Math.round((mem.heapTotal / 1024 / 1024) * 100) / 100,
      rssMB: Math.round((mem.rss / 1024 / 1024) * 100) / 100,
      externalMB: Math.round((mem.external / 1024 / 1024) * 100) / 100,
    }
  }

  return { heapUsedMB: 0, heapTotalMB: 0, rssMB: 0, externalMB: 0 }
}

/**
 * Builds a soak summary from accumulated iteration results.
 * @param {string} startedAt - ISO timestamp
 * @param {string} endedAt - ISO timestamp
 * @param {SoakIterationResult[]} iterations
 * @returns {SoakSummary}
 */
export function buildSoakSummary(startedAt, endedAt, iterations) {
  const totalRuns = iterations.length
  const passCount = iterations.filter((r) => r.status === 'pass').length
  const failCount = totalRuns - passCount
  const passRate = totalRuns > 0 ? Math.round((passCount / totalRuns) * 10000) / 100 : 0

  const durations = iterations.map((r) => r.durationMs)
  const totalDurationMs = durations.reduce((sum, d) => sum + d, 0)
  const avgIterationMs = totalRuns > 0 ? Math.round(totalDurationMs / totalRuns) : 0
  const minIterationMs = durations.length > 0 ? Math.min(...durations) : 0
  const maxIterationMs = durations.length > 0 ? Math.max(...durations) : 0
  const timingDriftMs = maxIterationMs - minIterationMs

  return {
    startedAt,
    endedAt,
    totalRuns,
    passCount,
    failCount,
    passRate,
    totalDurationMs,
    avgIterationMs,
    minIterationMs,
    maxIterationMs,
    timingDriftMs,
    iterations,
    memoryIndicators: collectMemoryIndicators(),
  }
}

/**
 * Runs a soak loop using a provided runner function.
 *
 * @param {SoakConfig} config
 * @param {(iteration: number) => Promise<{pass: boolean, eventCount: number, error?: string}>} runFn
 *   Function that executes one iteration. Must return pass/fail status and event count.
 * @returns {Promise<SoakSummary>}
 */
export async function runSoak(config, runFn) {
  const validated = validateSoakConfig(config)
  const startedAt = new Date().toISOString()
  const soakStartTime = Date.now()
  const iterations = []

  let iteration = 0
  while (shouldContinueSoak(iteration, Date.now() - soakStartTime, validated)) {
    const iterStart = Date.now()
    let status = 'pass'
    let eventCount = 0
    let error = null

    try {
      const result = await runFn(iteration)
      status = result.pass ? 'pass' : 'fail'
      eventCount = result.eventCount || 0
      error = result.error || null
    } catch (err) {
      status = 'fail'
      error = String(err?.message || err)
    }

    const durationMs = Date.now() - iterStart
    iterations.push({
      iteration,
      status,
      durationMs,
      eventCount,
      error,
    })

    iteration++

    // Cooldown between runs (skip after last)
    if (validated.cooldownMs > 0 && shouldContinueSoak(iteration, Date.now() - soakStartTime, validated)) {
      await sleep(validated.cooldownMs)
    }
  }

  const endedAt = new Date().toISOString()
  return buildSoakSummary(startedAt, endedAt, iterations)
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms))
}
