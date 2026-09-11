import { beforeEach, afterEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useWorkspaceLayoutStore, type WorkspaceExperience } from '../../store/workspaceLayoutStore'

describe('workspace layout preferences', () => {
  beforeEach(() => { window.localStorage.clear(); setActivePinia(createPinia()) })
  afterEach(() => vi.restoreAllMocks())

  it('defaults to Classic with Studio presentation', () => {
    expect(useWorkspaceLayoutStore().$state).toEqual({ experience: 'classic', presentation: 'studio' })
  })
  it('persists and restores independent experience and presentation choices', () => {
    const store = useWorkspaceLayoutStore()
    store.setPresentation('control')
    store.setExperience('companion')
    setActivePinia(createPinia())
    expect(useWorkspaceLayoutStore().$state).toEqual({ experience: 'companion', presentation: 'control' })
  })
  it.each(['{broken', 'null', '42', '{"experience":"admin","presentation":"invalid"}'])('ignores malformed preferences %s', (raw) => {
    window.localStorage.setItem('td.workspace.layout.v1', raw)
    expect(useWorkspaceLayoutStore().$state).toEqual({ experience: 'classic', presentation: 'studio' })
  })
  it('retains a valid field when the other saved field is invalid', () => {
    window.localStorage.setItem('td.workspace.layout.v1', '{"experience":"studio","presentation":"bad"}')
    expect(useWorkspaceLayoutStore().$state).toEqual({ experience: 'studio', presentation: 'studio' })
  })
  it('rejects invalid runtime choices', () => {
    const store = useWorkspaceLayoutStore()
    store.setExperience('unexpected' as WorkspaceExperience)
    expect(store.experience).toBe('classic')
  })
  it('keeps switching usable when storage is denied', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('denied') })
    const store = useWorkspaceLayoutStore()
    expect(() => store.setExperience('unified')).not.toThrow()
    expect(store.experience).toBe('unified')
  })
})
