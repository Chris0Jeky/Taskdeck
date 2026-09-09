import { createPinia, setActivePinia } from 'pinia'
import { reactive } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useWorkspaceExperimentStore } from '../../store/workspaceExperimentStore'

const session = reactive({ userId: 'first' as string | null })
vi.mock('../../store/sessionStore', () => ({ useSessionStore: () => session }))
const trial = { experience: 'studio', presentation: 'zen', theme: 'grove', ease: 4, note: 'I found the next step.' }

describe('workspace comparison observations', () => {
  beforeEach(() => { setActivePinia(createPinia()); session.userId = 'first'; localStorage.clear() })
  it('exports only manually recorded trials and keeps notes out of browser storage', () => {
    const store = useWorkspaceExperimentStore()
    expect(store.record(trial)).toBe(true)
    expect(JSON.parse(store.exportJson()).trials[0]).toMatchObject(trial)
    expect(localStorage.length).toBe(0)
  })
  it('clears observations immediately on identity change or sign out', () => {
    const store = useWorkspaceExperimentStore()
    store.record(trial)
    session.userId = 'second'
    expect(store.trials).toEqual([])
    store.record(trial)
    session.userId = null
    expect(store.trials).toEqual([])
    expect(store.record(trial)).toBe(false)
  })
  it('rejects invalid ratings and unrecognized experiences', () => {
    const store = useWorkspaceExperimentStore()
    expect(store.record({ ...trial, ease: 0 })).toBe(false)
    expect(store.record({ ...trial, ease: 4.5 })).toBe(false)
    expect(store.record({ ...trial, experience: 'automatic' })).toBe(false)
    expect(store.trials).toEqual([])
  })
})
