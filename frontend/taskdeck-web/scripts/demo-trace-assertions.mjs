/**
 * demo-trace-assertions.mjs
 *
 * Assertion utilities for comparing demo trace output against known-good
 * snapshots and validating structural expectations on trace events.
 */

/**
 * @typedef {'exact' | 'structural'} MatchMode
 */

/**
 * @typedef {object} TraceAssertionResult
 * @property {boolean} pass
 * @property {string[]} errors
 */

/**
 * Compares two trace snapshots using exact match mode.
 * Events must match in count, order, and content (by JSON equality).
 *
 * @param {Array<object>} actual - Actual trace events
 * @param {Array<object>} expected - Known-good trace events
 * @returns {TraceAssertionResult}
 */
export function assertTraceExactMatch(actual, expected) {
  const errors = []

  if (!Array.isArray(actual)) {
    return { pass: false, errors: ['Actual trace is not an array'] }
  }
  if (!Array.isArray(expected)) {
    return { pass: false, errors: ['Expected trace is not an array'] }
  }

  if (actual.length !== expected.length) {
    errors.push(
      `Event count mismatch: got ${actual.length}, expected ${expected.length}`,
    )
  }

  const limit = Math.min(actual.length, expected.length)
  for (let i = 0; i < limit; i++) {
    const actualJson = JSON.stringify(actual[i])
    const expectedJson = JSON.stringify(expected[i])
    if (actualJson !== expectedJson) {
      errors.push(
        `Event at index ${i} differs:\n  actual:   ${actualJson.slice(0, 200)}\n  expected: ${expectedJson.slice(0, 200)}`,
      )
    }
  }

  return { pass: errors.length === 0, errors }
}

/**
 * Compares two trace snapshots using structural/shape match mode.
 * Validates that each event has the same `type` field in order, but
 * ignores timestamps, IDs, and other volatile fields.
 *
 * @param {Array<object>} actual
 * @param {Array<object>} expected
 * @param {object} [options]
 * @param {string[]} [options.shapeFields] - Fields to compare beyond `type` (default: ['type'])
 * @returns {TraceAssertionResult}
 */
export function assertTraceStructuralMatch(actual, expected, options = {}) {
  const errors = []
  const shapeFields = options.shapeFields || ['type']

  if (!Array.isArray(actual)) {
    return { pass: false, errors: ['Actual trace is not an array'] }
  }
  if (!Array.isArray(expected)) {
    return { pass: false, errors: ['Expected trace is not an array'] }
  }

  if (actual.length !== expected.length) {
    errors.push(
      `Event count mismatch: got ${actual.length}, expected ${expected.length}`,
    )
  }

  const limit = Math.min(actual.length, expected.length)
  for (let i = 0; i < limit; i++) {
    for (const field of shapeFields) {
      const actualValue = actual[i]?.[field]
      const expectedValue = expected[i]?.[field]
      if (actualValue !== expectedValue) {
        errors.push(
          `Event[${i}].${field}: got "${actualValue}", expected "${expectedValue}"`,
        )
      }
    }
  }

  return { pass: errors.length === 0, errors }
}

/**
 * Validates that trace events contain a required ordered sequence of event types.
 * The sequence must appear in order, but other events may appear between them.
 *
 * @param {Array<object>} events
 * @param {string[]} requiredSequence - Ordered list of event types that must appear
 * @returns {TraceAssertionResult}
 */
export function assertTraceStepOrdering(events, requiredSequence) {
  const errors = []

  if (!Array.isArray(events)) {
    return { pass: false, errors: ['Events is not an array'] }
  }
  if (!Array.isArray(requiredSequence) || requiredSequence.length === 0) {
    return { pass: true, errors: [] }
  }

  let seqIndex = 0
  for (const event of events) {
    if (seqIndex >= requiredSequence.length) break
    if (String(event?.type || '') === requiredSequence[seqIndex]) {
      seqIndex++
    }
  }

  if (seqIndex < requiredSequence.length) {
    const missing = requiredSequence.slice(seqIndex)
    errors.push(
      `Required event sequence incomplete. Missing from index ${seqIndex}: [${missing.join(', ')}]`,
    )
  }

  return { pass: errors.length === 0, errors }
}

/**
 * Validates that no events with error-typed suffixes exist in the trace,
 * unless explicitly allowed.
 *
 * @param {Array<object>} events
 * @param {object} [options]
 * @param {string[]} [options.allowedErrorTypes] - Error types to ignore
 * @returns {TraceAssertionResult}
 */
export function assertNoUnexpectedErrors(events, options = {}) {
  const errors = []
  const allowed = new Set(options.allowedErrorTypes || [])

  if (!Array.isArray(events)) {
    return { pass: false, errors: ['Events is not an array'] }
  }

  for (let i = 0; i < events.length; i++) {
    const type = String(events[i]?.type || '')
    if (type.endsWith('.error') && !allowed.has(type)) {
      const detail = events[i]?.error || events[i]?.reason || ''
      errors.push(
        `Unexpected error event at index ${i}: ${type}${detail ? ` - ${String(detail).slice(0, 200)}` : ''}`,
      )
    }
  }

  return { pass: errors.length === 0, errors }
}

/**
 * Validates that required event types are present in the trace (in any order).
 *
 * @param {Array<object>} events
 * @param {string[]} requiredTypes - Event types that must appear at least once
 * @returns {TraceAssertionResult}
 */
export function assertRequiredEventsPresent(events, requiredTypes) {
  const errors = []

  if (!Array.isArray(events)) {
    return { pass: false, errors: ['Events is not an array'] }
  }

  const presentTypes = new Set(events.map((e) => String(e?.type || '')))
  for (const requiredType of requiredTypes) {
    if (!presentTypes.has(requiredType)) {
      errors.push(`Required event type missing: ${requiredType}`)
    }
  }

  return { pass: errors.length === 0, errors }
}

/**
 * Runs a full assertion suite against a trace, combining ordering, errors,
 * and required events checks.
 *
 * @param {Array<object>} events
 * @param {object} expectations
 * @param {string[]} [expectations.requiredSequence]
 * @param {string[]} [expectations.requiredEvents]
 * @param {string[]} [expectations.allowedErrorTypes]
 * @returns {TraceAssertionResult}
 */
export function assertTrace(events, expectations = {}) {
  const allErrors = []

  if (expectations.requiredSequence) {
    const result = assertTraceStepOrdering(events, expectations.requiredSequence)
    allErrors.push(...result.errors)
  }

  if (expectations.requiredEvents) {
    const result = assertRequiredEventsPresent(events, expectations.requiredEvents)
    allErrors.push(...result.errors)
  }

  const errorResult = assertNoUnexpectedErrors(events, {
    allowedErrorTypes: expectations.allowedErrorTypes,
  })
  allErrors.push(...errorResult.errors)

  return { pass: allErrors.length === 0, errors: allErrors }
}
