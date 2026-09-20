import { describe, expect, it } from 'vitest'
import { createAssignmentSaveRegistry } from '../../composables/useAssignmentSaveRegistry'

describe('assignment-save registry aggregation', () => {
  it('stays globally saving until the final operation owner settles', () => {
    const events: boolean[] = []
    const registry = createAssignmentSaveRegistry(saving => events.push(saving))

    const releaseA = registry.begin('column-a')
    expect(events).toEqual([true])

    const releaseB = registry.begin('column-b')
    expect(events).toEqual([true])

    releaseA()
    expect(events).toEqual([true])

    releaseB()
    expect(events).toEqual([true, false])
  })

  it('treats duplicate releases as idempotent and ignores old-generation releases', () => {
    const events: boolean[] = []
    const registry = createAssignmentSaveRegistry(saving => events.push(saving))

    const oldRelease = registry.begin('column-a')
    registry.reset()
    expect(events).toEqual([true, false])

    const newRelease = registry.begin('column-a')
    expect(events).toEqual([true, false, true])

    oldRelease()
    oldRelease()
    expect(events).toEqual([true, false, true])

    newRelease()
    newRelease()
    expect(events).toEqual([true, false, true, false])
  })
})
