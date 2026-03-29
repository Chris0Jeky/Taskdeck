import { describe, expect, it } from 'vitest'

import {
  escapeHtml,
  classifyEventStatus,
  extractTraceSteps,
  generateHtmlReport,
} from '../scripts/demo-report-html.mjs'

describe('demo HTML report generator', () => {
  describe('escapeHtml', () => {
    it('escapes HTML special characters', () => {
      expect(escapeHtml('<script>alert("xss")</script>')).toBe(
        '&lt;script&gt;alert(&quot;xss&quot;)&lt;/script&gt;',
      )
    })

    it('handles null and undefined gracefully', () => {
      expect(escapeHtml(null)).toBe('')
      expect(escapeHtml(undefined)).toBe('')
    })

    it('escapes ampersands and single quotes', () => {
      expect(escapeHtml("Tom & Jerry's")).toBe('Tom &amp; Jerry&#039;s')
    })
  })

  describe('classifyEventStatus', () => {
    it('classifies .ok events as pass', () => {
      expect(classifyEventStatus({ type: 'scenario.step.ok' })).toBe('pass')
    })

    it('classifies .error events as fail', () => {
      expect(classifyEventStatus({ type: 'scenario.step.error' })).toBe('fail')
    })

    it('classifies .skipped events as skip', () => {
      expect(classifyEventStatus({ type: 'scenario.step.skipped' })).toBe('skip')
    })

    it('classifies other events as info', () => {
      expect(classifyEventStatus({ type: 'scenario.start' })).toBe('info')
      expect(classifyEventStatus({})).toBe('info')
      expect(classifyEventStatus(null)).toBe('info')
    })
  })

  describe('extractTraceSteps', () => {
    it('maps events to step rows with correct status', () => {
      const events = [
        { type: 'scenario.start', ts: '2026-01-01T00:00:00Z' },
        { type: 'scenario.step.ok', ts: '2026-01-01T00:00:01Z', stepLabel: 'Create board' },
        { type: 'scenario.step.error', ts: '2026-01-01T00:00:02Z', error: 'timeout' },
      ]

      const steps = extractTraceSteps(events)
      expect(steps).toHaveLength(3)
      expect(steps[0].status).toBe('info')
      expect(steps[1].status).toBe('pass')
      expect(steps[1].label).toBe('Create board')
      expect(steps[2].status).toBe('fail')
      expect(steps[2].detail).toBe('timeout')
    })

    it('handles empty events array', () => {
      expect(extractTraceSteps([])).toEqual([])
    })
  })

  describe('generateHtmlReport', () => {
    it('produces valid self-contained HTML with scenario name and status', () => {
      const html = generateHtmlReport({
        runSummary: {
          runId: 'test-run-1',
          scenario: 'client-onboarding',
          status: 'ok',
          startedAt: '2026-01-01T00:00:00Z',
          endedAt: '2026-01-01T00:01:00Z',
          stats: { events: 5, proposals: 2, captures: 1 },
        },
        traceEvents: [
          { type: 'scenario.start', ts: '2026-01-01T00:00:00Z' },
          { type: 'scenario.step.ok', ts: '2026-01-01T00:00:01Z', stepLabel: 'Create board' },
        ],
        screenshots: [],
      })

      expect(html).toContain('<!DOCTYPE html>')
      expect(html).toContain('client-onboarding')
      expect(html).toContain('test-run-1')
      expect(html).toContain('status-pass')
      expect(html).toContain('PASS')
      expect(html).toContain('Create board')
      expect(html).toContain('No screenshots captured.')
      // Self-contained: no external link/script tags
      expect(html).not.toContain('<link rel="stylesheet"')
      expect(html).not.toContain('<script src=')
    })

    it('includes inline screenshots when provided', () => {
      const html = generateHtmlReport({
        runSummary: { runId: 'r1', scenario: 'test', status: 'ok', stats: {} },
        traceEvents: [],
        screenshots: [
          { name: 'step1.png', dataUrl: 'data:image/png;base64,abc123' },
        ],
      })

      expect(html).toContain('step1.png')
      expect(html).toContain('data:image/png;base64,abc123')
      expect(html).not.toContain('No screenshots captured.')
    })

    it('renders fail status for error runs', () => {
      const html = generateHtmlReport({
        runSummary: { runId: 'r2', scenario: 'test', status: 'error', stats: {} },
        traceEvents: [{ type: 'scenario.step.error', error: 'boom' }],
        screenshots: [],
      })

      expect(html).toContain('status-fail')
      expect(html).toContain('FAIL')
      expect(html).toContain('boom')
    })

    it('escapes HTML in user-provided data to prevent injection', () => {
      const html = generateHtmlReport({
        runSummary: {
          runId: '<script>alert(1)</script>',
          scenario: '<img onerror=alert(1)>',
          status: 'ok',
          stats: {},
        },
        traceEvents: [],
        screenshots: [],
      })

      expect(html).not.toContain('<script>alert(1)</script>')
      expect(html).toContain('&lt;script&gt;')
    })
  })
})
