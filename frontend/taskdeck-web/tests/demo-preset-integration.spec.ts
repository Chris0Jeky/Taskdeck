import { describe, expect, it } from 'vitest'

import { requirePreset, mergePresetArgs } from '../scripts/demo-director-presets.mjs'
import { assertTrace } from '../scripts/demo-trace-assertions.mjs'
import { generateHtmlReport, extractTraceSteps } from '../scripts/demo-report-html.mjs'
import { buildSoakSummary } from '../scripts/demo-soak.mjs'

describe('preset scenario integration', () => {
  it('runs the happy-path-capture preset through the full assertion and reporting pipeline', () => {
    // Load preset
    const preset = requirePreset('happy-path-capture')
    expect(preset.scenario).toBe('client-onboarding')

    // Merge args (no overrides)
    const args = mergePresetArgs(preset)
    expect(args.skipLlm).toBe(true)
    expect(args.turns).toBe(0)

    // Simulate a successful trace that matches preset expectations
    const simulatedTrace = [
      { type: 'scenario.start', ts: '2026-01-01T00:00:00Z' },
      { type: 'scenario.step.ok', ts: '2026-01-01T00:00:01Z', stepLabel: 'Create board', stepType: 'createBoard' },
      { type: 'scenario.step.ok', ts: '2026-01-01T00:00:02Z', stepLabel: 'Apply starter pack', stepType: 'applyStarterPack' },
      { type: 'scenario.step.ok', ts: '2026-01-01T00:00:03Z', stepLabel: 'Create capture', stepType: 'createCapture' },
      { type: 'scenario.step.skipped', ts: '2026-01-01T00:00:04Z', stepLabel: 'Triage capture', reason: 'requiresLlm' },
      { type: 'scenario.end', ts: '2026-01-01T00:00:05Z' },
    ]

    // Validate trace against preset expectations
    const assertionResult = assertTrace(simulatedTrace, preset.expectations)
    expect(assertionResult.pass).toBe(true)
    expect(assertionResult.errors).toEqual([])

    // Generate HTML report from the same data
    const html = generateHtmlReport({
      runSummary: {
        runId: 'integration-test-1',
        scenario: preset.scenario,
        status: 'ok',
        startedAt: '2026-01-01T00:00:00Z',
        endedAt: '2026-01-01T00:00:05Z',
        stats: { events: simulatedTrace.length, proposals: 0, captures: 1 },
      },
      traceEvents: simulatedTrace,
      screenshots: [],
    })

    expect(html).toContain('client-onboarding')
    expect(html).toContain('Create board')
    expect(html).toContain('PASS')
    expect(html).toContain('SKIP')

    // Verify trace steps extraction
    const steps = extractTraceSteps(simulatedTrace)
    expect(steps).toHaveLength(6)
    expect(steps[0].status).toBe('info') // scenario.start
    expect(steps[1].status).toBe('pass') // step.ok
    expect(steps[4].status).toBe('skip') // step.skipped
  })

  it('detects assertion failures when trace does not match preset expectations', () => {
    const preset = requirePreset('happy-path-capture')

    // Simulate a trace with an unexpected error
    const badTrace = [
      { type: 'scenario.start', ts: '2026-01-01T00:00:00Z' },
      { type: 'scenario.step.error', ts: '2026-01-01T00:00:01Z', error: 'board creation failed' },
    ]

    const result = assertTrace(badTrace, preset.expectations)
    expect(result.pass).toBe(false)
    expect(result.errors.length).toBeGreaterThan(0)
    // Should flag missing scenario.end and the unexpected error
    expect(result.errors.some((e: string) => e.includes('scenario.end'))).toBe(true)
    expect(result.errors.some((e: string) => e.includes('scenario.step.error'))).toBe(true)
  })

  it('integrates soak summary with preset reporting', () => {
    const iterations = [
      { iteration: 0, status: 'pass' as const, durationMs: 120, eventCount: 6, error: null },
      { iteration: 1, status: 'pass' as const, durationMs: 130, eventCount: 6, error: null },
      { iteration: 2, status: 'fail' as const, durationMs: 200, eventCount: 2, error: 'timeout' },
    ]

    const summary = buildSoakSummary('2026-01-01T00:00:00Z', '2026-01-01T00:01:00Z', iterations)

    // Verify the soak summary can feed into an HTML report
    const html = generateHtmlReport({
      runSummary: {
        runId: 'soak-integration-test',
        scenario: 'client-onboarding',
        status: summary.failCount > 0 ? 'error' : 'ok',
        startedAt: summary.startedAt,
        endedAt: summary.endedAt,
        stats: {
          events: summary.totalRuns,
          proposals: 0,
          captures: 0,
        },
      },
      traceEvents: iterations.map((iter) => ({
        type: iter.status === 'pass' ? 'soak.iteration.ok' : 'soak.iteration.error',
        ts: summary.startedAt,
        error: iter.error,
      })),
      screenshots: [],
    })

    expect(html).toContain('soak-integration-test')
    expect(html).toContain('status-fail') // because failCount > 0
  })
})
