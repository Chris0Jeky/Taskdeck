import { describe, expect, it } from 'vitest'
import { drainCleanups } from './drainCleanups'

describe('drainCleanups', () => {
  it('leaves an empty cleanup stack unchanged', () => {
    const cleanups: Array<() => void> = []

    expect(() => drainCleanups(cleanups)).not.toThrow()
    expect(cleanups).toHaveLength(0)
  })

  it('runs every callback in LIFO order and empties the stack', () => {
    const events: string[] = []
    const cleanups: Array<() => void> = [
      () => events.push('oldest'),
      () => events.push('middle'),
      () => events.push('newest'),
    ]

    drainCleanups(cleanups)

    expect(events).toEqual(['newest', 'middle', 'oldest'])
    expect(cleanups).toHaveLength(0)
  })

  it('rethrows the original error when one callback fails', () => {
    const failure = new Error('cleanup failed')
    const cleanups: Array<() => void> = [
      () => undefined,
      () => {
        throw failure
      },
    ]

    expect(() => drainCleanups(cleanups)).toThrow(failure)
    expect(cleanups).toHaveLength(0)
  })

  it('aggregates multiple failures after running every callback', () => {
    const oldestFailure = new Error('oldest cleanup failed')
    const newestFailure = new Error('newest cleanup failed')
    const events: string[] = []
    const cleanups: Array<() => void> = [
      () => {
        events.push('oldest')
        throw oldestFailure
      },
      () => {
        events.push('newest')
        throw newestFailure
      },
    ]

    let thrown: unknown
    try {
      drainCleanups(cleanups)
    } catch (error: unknown) {
      thrown = error
    }

    expect(thrown).toBeInstanceOf(AggregateError)
    expect((thrown as AggregateError).errors).toEqual([newestFailure, oldestFailure])
    expect(events).toEqual(['newest', 'oldest'])
    expect(cleanups).toHaveLength(0)
  })
})
