import { describe, expect, it } from 'vitest'

import {
  assertTraceExactMatch,
  assertTraceStructuralMatch,
  assertTraceStepOrdering,
  assertNoUnexpectedErrors,
  assertRequiredEventsPresent,
  assertTrace,
} from '../scripts/demo-trace-assertions.mjs'

describe('demo trace assertions', () => {
  describe('assertTraceExactMatch', () => {
    it('passes when traces are identical', () => {
      const events = [{ type: 'a', ts: '1' }, { type: 'b', ts: '2' }]
      const result = assertTraceExactMatch(events, JSON.parse(JSON.stringify(events)))
      expect(result.pass).toBe(true)
      expect(result.errors).toEqual([])
    })

    it('fails on event count mismatch', () => {
      const result = assertTraceExactMatch([{ type: 'a' }], [{ type: 'a' }, { type: 'b' }])
      expect(result.pass).toBe(false)
      expect(result.errors[0]).toContain('Event count mismatch')
    })

    it('fails on content mismatch at specific index', () => {
      const result = assertTraceExactMatch(
        [{ type: 'a', ts: '1' }],
        [{ type: 'a', ts: '2' }],
      )
      expect(result.pass).toBe(false)
      expect(result.errors[0]).toContain('index 0')
    })

    it('rejects non-array inputs', () => {
      expect(assertTraceExactMatch(null as any, []).pass).toBe(false)
      expect(assertTraceExactMatch([], null as any).pass).toBe(false)
    })
  })

  describe('assertTraceStructuralMatch', () => {
    it('passes when event types match in order regardless of other fields', () => {
      const actual = [{ type: 'a', ts: 'now', id: '1' }, { type: 'b', ts: 'later', id: '2' }]
      const expected = [{ type: 'a', ts: 'then', id: '99' }, { type: 'b', ts: 'whenever', id: '100' }]
      const result = assertTraceStructuralMatch(actual, expected)
      expect(result.pass).toBe(true)
    })

    it('fails when event types differ', () => {
      const result = assertTraceStructuralMatch(
        [{ type: 'a' }, { type: 'c' }],
        [{ type: 'a' }, { type: 'b' }],
      )
      expect(result.pass).toBe(false)
      expect(result.errors[0]).toContain('Event[1].type')
    })

    it('supports custom shape fields', () => {
      const actual = [{ type: 'a', stepType: 'create' }]
      const expected = [{ type: 'a', stepType: 'delete' }]
      const result = assertTraceStructuralMatch(actual, expected, {
        shapeFields: ['type', 'stepType'],
      })
      expect(result.pass).toBe(false)
      expect(result.errors[0]).toContain('stepType')
    })
  })

  describe('assertTraceStepOrdering', () => {
    it('passes when required sequence appears in order', () => {
      const events = [
        { type: 'scenario.start' },
        { type: 'noise' },
        { type: 'scenario.step.ok' },
        { type: 'more.noise' },
        { type: 'scenario.end' },
      ]
      const result = assertTraceStepOrdering(events, [
        'scenario.start',
        'scenario.step.ok',
        'scenario.end',
      ])
      expect(result.pass).toBe(true)
    })

    it('fails when sequence is incomplete', () => {
      const events = [{ type: 'scenario.start' }, { type: 'scenario.end' }]
      const result = assertTraceStepOrdering(events, [
        'scenario.start',
        'scenario.step.ok',
        'scenario.end',
      ])
      expect(result.pass).toBe(false)
      expect(result.errors[0]).toContain('Missing from index 1')
    })

    it('passes with empty required sequence', () => {
      expect(assertTraceStepOrdering([], []).pass).toBe(true)
    })
  })

  describe('assertNoUnexpectedErrors', () => {
    it('passes when no error events exist', () => {
      const events = [{ type: 'scenario.start' }, { type: 'scenario.step.ok' }]
      expect(assertNoUnexpectedErrors(events).pass).toBe(true)
    })

    it('fails when unexpected error events exist', () => {
      const events = [
        { type: 'scenario.start' },
        { type: 'scenario.step.error', error: 'timeout' },
      ]
      const result = assertNoUnexpectedErrors(events)
      expect(result.pass).toBe(false)
      expect(result.errors[0]).toContain('scenario.step.error')
      expect(result.errors[0]).toContain('timeout')
    })

    it('allows explicitly permitted error types', () => {
      const events = [{ type: 'autopilot.turn.error' }]
      const result = assertNoUnexpectedErrors(events, {
        allowedErrorTypes: ['autopilot.turn.error'],
      })
      expect(result.pass).toBe(true)
    })
  })

  describe('assertRequiredEventsPresent', () => {
    it('passes when all required types are present', () => {
      const events = [
        { type: 'scenario.start' },
        { type: 'scenario.step.ok' },
        { type: 'scenario.end' },
      ]
      const result = assertRequiredEventsPresent(events, ['scenario.start', 'scenario.end'])
      expect(result.pass).toBe(true)
    })

    it('fails when required type is missing', () => {
      const events = [{ type: 'scenario.start' }]
      const result = assertRequiredEventsPresent(events, ['scenario.start', 'scenario.end'])
      expect(result.pass).toBe(false)
      expect(result.errors[0]).toContain('scenario.end')
    })
  })

  describe('assertTrace (combined)', () => {
    it('runs all checks and aggregates errors', () => {
      const events = [
        { type: 'scenario.start' },
        { type: 'scenario.step.error', error: 'boom' },
      ]

      const result = assertTrace(events, {
        requiredSequence: ['scenario.start', 'scenario.end'],
        requiredEvents: ['scenario.end'],
        allowedErrorTypes: [],
      })

      expect(result.pass).toBe(false)
      // Should have errors from ordering, required events, and unexpected errors
      expect(result.errors.length).toBeGreaterThanOrEqual(3)
    })

    it('passes a clean trace with valid expectations', () => {
      const events = [
        { type: 'scenario.start' },
        { type: 'scenario.step.ok' },
        { type: 'scenario.end' },
      ]

      const result = assertTrace(events, {
        requiredSequence: ['scenario.start', 'scenario.end'],
        requiredEvents: ['scenario.start', 'scenario.end'],
      })
      expect(result.pass).toBe(true)
    })
  })
})
