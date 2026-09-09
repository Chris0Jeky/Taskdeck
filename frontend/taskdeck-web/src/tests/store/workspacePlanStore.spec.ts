import { createPinia, setActivePinia } from 'pinia'
import { reactive } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useWorkspacePlanStore } from '../../store/workspacePlanStore'
import { workspacePlanApi, type WorkspacePlan } from '../../api/workspacePlanApi'

const session = reactive({ userId: 'first' as string | null })
vi.mock('../../store/sessionStore', () => ({ useSessionStore: () => session }))
vi.mock('../../api/workspacePlanApi', () => ({ workspacePlanApi: { get: vi.fn(), save: vi.fn(), focus: vi.fn() } }))
const initial: WorkspacePlan = { revision: 3, entries: [], lastWorked: null }

describe('private personal plan', () => {
  beforeEach(() => { vi.resetAllMocks(); setActivePinia(createPinia()); session.userId = 'first'; localStorage.clear() })
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
  it('requires reload after a lost or conflicting write response', async () => {
    vi.mocked(workspacePlanApi.get).mockResolvedValue(initial)
    vi.mocked(workspacePlanApi.save).mockRejectedValue(new Error('Conflict'))
    const store = useWorkspacePlanStore()
    await store.load()
    expect(await store.save([])).toBe(false)
    expect(store.ready).toBe(false)
    expect(store.error).toBeTruthy()
    expect(await store.focus('board', 'card')).toBe(false)
    expect(workspacePlanApi.focus).not.toHaveBeenCalled()
    await store.load()
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
