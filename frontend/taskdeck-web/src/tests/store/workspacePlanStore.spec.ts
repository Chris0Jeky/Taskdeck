import { createPinia, setActivePinia } from 'pinia'
import { reactive } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useWorkspacePlanStore } from '../../store/workspacePlanStore'
import { workspacePlanApi, type WorkspacePlan } from '../../api/workspacePlanApi'

const session = reactive({ userId: 'first' as string | null })
const demo = vi.hoisted(() => ({ enabled: false }))
vi.mock('../../utils/demoMode', () => ({ get isDemoMode() { return demo.enabled } }))
vi.mock('../../store/sessionStore', () => ({ useSessionStore: () => session }))
vi.mock('../../api/workspacePlanApi', () => ({ workspacePlanApi: { get: vi.fn(), save: vi.fn(), focus: vi.fn() } }))
const initial: WorkspacePlan = { revision: 3, entries: [], lastWorked: null }

describe('private personal plan', () => {
  beforeEach(() => { vi.resetAllMocks(); setActivePinia(createPinia()); session.userId = 'first'; demo.enabled = false; localStorage.clear() })
  it('does not read or write a server plan in a backend-less demo build', async () => {
    demo.enabled = true
    const store = useWorkspacePlanStore()
    await store.load()
    expect(store.available).toBe(false)
    expect(await store.save([])).toBe(false)
    expect(await store.focus('board', 'card')).toBe(false)
    expect(workspacePlanApi.get).not.toHaveBeenCalled()
    expect(workspacePlanApi.save).not.toHaveBeenCalled()
    expect(workspacePlanApi.focus).not.toHaveBeenCalled()
  })
  it('uses the saved revision and strips displayed metadata from a write', async () => {
    vi.mocked(workspacePlanApi.get).mockResolvedValue(initial)
    vi.mocked(workspacePlanApi.save).mockResolvedValue({ ...initial, revision: 4 })
    const store = useWorkspacePlanStore()
    await store.load()
    const entry = { boardId: 'board', cardId: 'card', plannedDate: '2026-09-10', title: 'Do not send display data' }
    expect(await store.save([entry])).toBe(true)
    expect(workspacePlanApi.save).toHaveBeenCalledWith(3, [{ boardId: 'board', cardId: 'card', plannedDate: '2026-09-10' }])
    expect(store.plan?.revision).toBe(4)
    expect(localStorage.length).toBe(0)
  })
  it('blocks writes until loaded and serializes focus and plan edits', async () => {
    const store = useWorkspacePlanStore()
    expect(await store.save([])).toBe(false)
    vi.mocked(workspacePlanApi.get).mockResolvedValue(initial)
    await store.load()
    let settle!: (value: WorkspacePlan) => void
    vi.mocked(workspacePlanApi.focus).mockReturnValue(new Promise(resolve => { settle = resolve }))
    const pending = store.focus('board', 'card')
    expect(await store.save([])).toBe(false)
    await store.load()
    expect(workspacePlanApi.get).toHaveBeenCalledTimes(1)
    settle({ ...initial, revision: 4 })
    expect(await pending).toBe(true)
  })
  it.each(['save', 'focus'] as const)('clears metadata and requires reload after an uncertain %s response', async (operation) => {
    const saved: WorkspacePlan = { ...initial, entries: [{ boardId: 'board', cardId: 'card', plannedDate: '2026-09-10', available: true, title: 'Private title', boardName: 'Private board', columnName: null, dueDate: null, isBlocked: false, blockReason: null }] }
    vi.mocked(workspacePlanApi.get).mockResolvedValue(saved)
    vi.mocked(workspacePlanApi[operation]).mockRejectedValue(new Error('Conflict'))
    const store = useWorkspacePlanStore()
    await store.load()
    expect(store.plan?.entries[0]?.title).toBe('Private title')
    expect(await (operation === 'save' ? store.save([]) : store.focus('board', 'card'))).toBe(false)
    expect(store.ready).toBe(false)
    expect(store.plan).toBeNull()
    expect(store.error).toContain(operation === 'save' ? 'Plan change could not be confirmed.' : 'Focus could not be confirmed.')
    expect(store.error).toContain('Refresh your plan before trying again.')
    vi.mocked(workspacePlanApi.focus).mockClear()
    expect(await store.focus('board', 'card')).toBe(false)
    expect(workspacePlanApi.focus).not.toHaveBeenCalled()
    await store.load()
    expect(store.ready).toBe(true)
    expect(store.plan?.entries[0]?.title).toBe('Private title')
  })
  it('shares overlapping Home and plan reads until the request settles', async () => {
    let settle!: (value: WorkspacePlan) => void
    vi.mocked(workspacePlanApi.get).mockReturnValueOnce(new Promise(resolve => { settle = resolve }))
    const store = useWorkspacePlanStore()
    const home = store.load()
    const plan = store.load()
    expect(workspacePlanApi.get).toHaveBeenCalledTimes(1)
    expect(store.loading).toBe(true)
    settle(initial)
    await Promise.all([home, plan])
    expect(store.ready).toBe(true)
    expect(store.loading).toBe(false)
    vi.mocked(workspacePlanApi.get).mockResolvedValue({ ...initial, revision: 4 })
    await store.load()
    expect(workspacePlanApi.get).toHaveBeenCalledTimes(2)
    expect(store.plan?.revision).toBe(4)
  })
  it('keeps a new-account read shared when the old account request settles', async () => {
    let old!: (value: WorkspacePlan) => void
    let current!: (value: WorkspacePlan) => void
    vi.mocked(workspacePlanApi.get)
      .mockReturnValueOnce(new Promise(resolve => { old = resolve }))
      .mockReturnValueOnce(new Promise(resolve => { current = resolve }))
    const store = useWorkspacePlanStore()
    const first = store.load()
    session.userId = 'second'
    const second = store.load()
    old(initial)
    await first
    expect(store.plan).toBeNull()
    expect(store.loading).toBe(true)
    const third = store.load()
    expect(workspacePlanApi.get).toHaveBeenCalledTimes(2)
    current({ ...initial, revision: 8 })
    await Promise.all([second, third])
    expect(store.plan?.revision).toBe(8)
    expect(store.ready).toBe(true)
  })
  it('discards a delayed old-account read and clears the current account on signout', async () => {
    let settle!: (value: WorkspacePlan) => void
    vi.mocked(workspacePlanApi.get).mockReturnValueOnce(new Promise(resolve => { settle = resolve }))
    const store = useWorkspacePlanStore()
    const pending = store.load()
    session.userId = 'second'
    settle(initial)
    await pending
    expect(store.plan).toBeNull()
    vi.mocked(workspacePlanApi.get).mockResolvedValue(initial)
    await store.load()
    session.userId = null
    expect(store.plan).toBeNull()
    expect(store.ready).toBe(false)
    expect(await store.save([])).toBe(false)
  })
  it('never navigates on an old-account focus completion', async () => {
    vi.mocked(workspacePlanApi.get).mockResolvedValue(initial)
    const store = useWorkspacePlanStore()
    await store.load()
    let settle!: (value: WorkspacePlan) => void
    vi.mocked(workspacePlanApi.focus).mockReturnValue(new Promise(resolve => { settle = resolve }))
    const pending = store.focus('board', 'card')
    session.userId = 'second'
    settle(initial)
    expect(await pending).toBe(false)
    expect(store.plan).toBeNull()
  })
  it('hides previously readable metadata when refresh fails', async () => {
    vi.mocked(workspacePlanApi.get).mockResolvedValue(initial)
    const store = useWorkspacePlanStore()
    await store.load()
    vi.mocked(workspacePlanApi.get).mockRejectedValue(new Error('Permission changed'))
    await store.load()
    expect(store.plan).toBeNull()
    expect(store.ready).toBe(false)
    expect(store.loading).toBe(false)
    expect(store.error).toBeTruthy()
  })
})
