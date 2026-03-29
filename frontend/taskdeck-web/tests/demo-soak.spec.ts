import { describe, expect, it } from 'vitest'

import {
  defaultSoakConfig,
  validateSoakConfig,
  shouldContinueSoak,
  buildSoakSummary,
  runSoak,
} from '../scripts/demo-soak.mjs'

describe('demo soak mode', () => {
  describe('defaultSoakConfig', () => {
    it('returns sensible defaults', () => {
      const config = defaultSoakConfig()
      expect(config.maxIterations).toBe(10)
      expect(config.maxDurationMs).toBe(0)
      expect(config.cooldownMs).toBe(500)
    })
  })

  describe('validateSoakConfig', () => {
    it('accepts valid config', () => {
      const config = validateSoakConfig({ maxIterations: 5, maxDurationMs: 0, cooldownMs: 100 })
      expect(config.maxIterations).toBe(5)
    })

    it('rejects config with both limits at zero', () => {
      expect(() =>
        validateSoakConfig({ maxIterations: 0, maxDurationMs: 0, cooldownMs: 0 }),
      ).toThrow('at least one of maxIterations')
    })

    it('rejects negative maxIterations', () => {
      expect(() =>
        validateSoakConfig({ maxIterations: -1, maxDurationMs: 1000, cooldownMs: 0 }),
      ).toThrow('Invalid maxIterations')
    })

    it('rejects negative maxDurationMs', () => {
      expect(() =>
        validateSoakConfig({ maxIterations: 1, maxDurationMs: -1, cooldownMs: 0 }),
      ).toThrow('Invalid maxDurationMs')
    })

    it('rejects negative cooldownMs', () => {
      expect(() =>
        validateSoakConfig({ maxIterations: 1, maxDurationMs: 0, cooldownMs: -1 }),
      ).toThrow('Invalid cooldownMs')
    })
  })

  describe('shouldContinueSoak', () => {
    it('stops when maxIterations is reached', () => {
      const config = { maxIterations: 3, maxDurationMs: 0, cooldownMs: 0 }
      expect(shouldContinueSoak(0, 0, config)).toBe(true)
      expect(shouldContinueSoak(2, 0, config)).toBe(true)
      expect(shouldContinueSoak(3, 0, config)).toBe(false)
    })

    it('stops when maxDurationMs is exceeded', () => {
      const config = { maxIterations: 0, maxDurationMs: 5000, cooldownMs: 0 }
      expect(shouldContinueSoak(0, 0, config)).toBe(true)
      expect(shouldContinueSoak(0, 4999, config)).toBe(true)
      expect(shouldContinueSoak(0, 5000, config)).toBe(false)
    })

    it('continues when neither limit is reached', () => {
      const config = { maxIterations: 10, maxDurationMs: 60000, cooldownMs: 0 }
      expect(shouldContinueSoak(5, 30000, config)).toBe(true)
    })
  })

  describe('buildSoakSummary', () => {
    it('computes correct statistics from iteration results', () => {
      const iterations = [
        { iteration: 0, status: 'pass', durationMs: 100, eventCount: 5, error: null },
        { iteration: 1, status: 'pass', durationMs: 150, eventCount: 6, error: null },
        { iteration: 2, status: 'fail', durationMs: 200, eventCount: 3, error: 'timeout' },
      ]

      const summary = buildSoakSummary('2026-01-01T00:00:00Z', '2026-01-01T00:01:00Z', iterations)

      expect(summary.totalRuns).toBe(3)
      expect(summary.passCount).toBe(2)
      expect(summary.failCount).toBe(1)
      expect(summary.passRate).toBeCloseTo(66.67, 1)
      expect(summary.totalDurationMs).toBe(450)
      expect(summary.avgIterationMs).toBe(150)
      expect(summary.minIterationMs).toBe(100)
      expect(summary.maxIterationMs).toBe(200)
      expect(summary.timingDriftMs).toBe(100)
      expect(summary.iterations).toHaveLength(3)
      expect(summary.memoryIndicators).toHaveProperty('heapUsedMB')
    })

    it('handles empty iterations', () => {
      const summary = buildSoakSummary('2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z', [])

      expect(summary.totalRuns).toBe(0)
      expect(summary.passCount).toBe(0)
      expect(summary.failCount).toBe(0)
      expect(summary.passRate).toBe(0)
      expect(summary.avgIterationMs).toBe(0)
      expect(summary.minIterationMs).toBe(0)
      expect(summary.maxIterationMs).toBe(0)
    })

    it('handles all-pass runs', () => {
      const iterations = [
        { iteration: 0, status: 'pass', durationMs: 50, eventCount: 2, error: null },
        { iteration: 1, status: 'pass', durationMs: 60, eventCount: 3, error: null },
      ]

      const summary = buildSoakSummary('2026-01-01T00:00:00Z', '2026-01-01T00:01:00Z', iterations)
      expect(summary.passRate).toBe(100)
      expect(summary.failCount).toBe(0)
    })
  })

  describe('runSoak', () => {
    it('executes the configured number of iterations', async () => {
      const calls: number[] = []
      const summary = await runSoak(
        { maxIterations: 3, maxDurationMs: 0, cooldownMs: 0 },
        async (iteration) => {
          calls.push(iteration)
          return { pass: true, eventCount: 2 }
        },
      )

      expect(calls).toEqual([0, 1, 2])
      expect(summary.totalRuns).toBe(3)
      expect(summary.passCount).toBe(3)
      expect(summary.passRate).toBe(100)
    })

    it('records failures from the runner function', async () => {
      const summary = await runSoak(
        { maxIterations: 2, maxDurationMs: 0, cooldownMs: 0 },
        async (iteration) => {
          if (iteration === 1) {
            return { pass: false, eventCount: 0, error: 'simulated failure' }
          }
          return { pass: true, eventCount: 5 }
        },
      )

      expect(summary.totalRuns).toBe(2)
      expect(summary.passCount).toBe(1)
      expect(summary.failCount).toBe(1)
      expect(summary.iterations[1].error).toBe('simulated failure')
    })

    it('catches runner exceptions as failures', async () => {
      const summary = await runSoak(
        { maxIterations: 1, maxDurationMs: 0, cooldownMs: 0 },
        async () => {
          throw new Error('unexpected crash')
        },
      )

      expect(summary.totalRuns).toBe(1)
      expect(summary.failCount).toBe(1)
      expect(summary.iterations[0].error).toBe('unexpected crash')
    })

    it('respects maxDurationMs to stop early', async () => {
      let _iteration = 0
      const summary = await runSoak(
        { maxIterations: 1000, maxDurationMs: 100, cooldownMs: 0 },
        async () => {
          _iteration++
          // Simulate some work
          await new Promise((resolve) => setTimeout(resolve, 30))
          return { pass: true, eventCount: 1 }
        },
      )

      // Should have stopped well before 1000 iterations due to time limit
      expect(summary.totalRuns).toBeLessThan(1000)
      expect(summary.totalRuns).toBeGreaterThan(0)
    })

    it('includes timing metrics in the summary', async () => {
      const summary = await runSoak(
        { maxIterations: 2, maxDurationMs: 0, cooldownMs: 0 },
        async () => {
          return { pass: true, eventCount: 1 }
        },
      )

      expect(summary.startedAt).toBeTruthy()
      expect(summary.endedAt).toBeTruthy()
      expect(summary.avgIterationMs).toBeGreaterThanOrEqual(0)
      expect(summary.timingDriftMs).toBeGreaterThanOrEqual(0)
    })
  })
})
