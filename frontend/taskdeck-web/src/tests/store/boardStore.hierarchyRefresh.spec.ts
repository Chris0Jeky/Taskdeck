import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { useBoardStore } from '../../store/boardStore'
import { boardsApi } from '../../api/boardsApi'
import { cardsApi } from '../../api/cardsApi'
import { labelsApi } from '../../api/labelsApi'
import type { BoardDetail, Card } from '../../types/board'
const { warning, error, success } = vi.hoisted(() => ({ warning: vi.fn(), error: vi.fn(), success: vi.fn() }))
vi.mock('../../api/boardsApi', () => ({ boardsApi: { getBoard: vi.fn() } }))
vi.mock('../../api/cardsApi', () => ({ cardsApi: { getCards: vi.fn(), setArchived: vi.fn(), deleteCard: vi.fn() } }))
vi.mock('../../api/labelsApi', () => ({ labelsApi: { getLabels: vi.fn() } }))
vi.mock('../../store/toastStore', () => ({ useToastStore: () => ({ warning, error, success }) }))
vi.mock('../../utils/demoMode', async (original) => ({ ...await original<typeof import('../../utils/demoMode')>(), isDemoMode: false }))
function deferred<T>() { let resolve!: (value: T) => void; let reject!: (reason: unknown) => void; const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no }); return { promise, resolve, reject } }
function board(id: string): BoardDetail { return { id, name: id, columns: [{ id: `col-${id}`, boardId: id, name: 'Todo', position: 0, cardCount: 2, wipLimit: null }] } as BoardDetail }
function card(id: string, boardId: string, parentCardId: string | null = null): Card { return { id, boardId, columnId: `col-${boardId}`, title: id, parentCardId, labels: [], updatedAt: '2026-01-01T00:00:00Z' } as unknown as Card }
for (const action of ['archive', 'delete'] as const) {
 describe(`hierarchy refresh after ${action}`, () => {
  beforeEach(() => {
    setActivePinia(createPinia()); vi.clearAllMocks()
    vi.mocked(boardsApi.getBoard).mockImplementation(async id => board(id))
    vi.mocked(labelsApi.getLabels).mockResolvedValue([])
    vi.mocked(cardsApi.setArchived).mockResolvedValue({ ...card('parent', 'A'), isArchived: true })
    vi.mocked(cardsApi.deleteCard).mockResolvedValue(undefined)
  })
  function start() {
    const store = useBoardStore(); store.currentBoard = board('A'); store.currentBoardCards = [card('parent', 'A'), card('child', 'A', 'parent')]
    const pending = action === 'archive' ? store.setCardArchived('A', 'parent', true, '2026-01-01T00:00:00Z', 'v1:pin') : store.deleteCard('A', 'parent')
    return { store, pending }
  }
  it.each(['navigation', 'logout', 'new account'] as const)('drops old child payload after %s', async (transition) => {
    const old = deferred<Card[]>()
    vi.mocked(cardsApi.getCards).mockImplementation(id => id === 'A' ? old.promise : Promise.resolve([card('new-account-card', 'B')]))
    const { store, pending } = start(); await flushPromises()
    if (transition !== 'navigation') store.resetForLogout()
    if (transition !== 'logout') await store.fetchBoard('B')
    old.resolve([card('child', 'A')]); await pending; await flushPromises()
    expect(store.currentBoard?.id ?? null).toBe(transition === 'logout' ? null : 'B')
    expect(store.currentBoardCards.map(c => c.id)).toEqual(transition === 'logout' ? [] : ['new-account-card'])
    if (store.currentBoard) {
      expect(store.currentBoard.columns[0]?.cardCount).toBe(1)
      expect(store.cardsByColumn.get('col-B')?.map(c => c.id)).toEqual(['new-account-card'])
    }
    expect(store.totalCardCount).toBe(transition === 'logout' ? 0 : 1)
    expect(warning).not.toHaveBeenCalled(); expect(error).not.toHaveBeenCalled()
  })
  it.each(['navigation', 'logout', 'new account'] as const)('does not report a stale read failure after %s', async (transition) => {
    const old = deferred<Card[]>(); const next = deferred<Card[]>()
    vi.mocked(cardsApi.getCards).mockImplementation(id => id === 'A' ? old.promise : next.promise)
    const { store, pending } = start(); await flushPromises()
    const outcome = pending.then(() => 'committed', () => 'rejected')
    if (transition !== 'navigation') store.resetForLogout()
    const navigation = transition === 'logout' ? Promise.resolve(false) : store.fetchBoard('B'); await flushPromises()
    old.reject(new Error('old account offline')); await outcome; await flushPromises()
    expect(store.loading).toBe(transition !== 'logout'); expect(store.error).toBeNull()
    expect(warning).not.toHaveBeenCalled(); expect(error).not.toHaveBeenCalled()
    next.resolve([card('new-account-card', 'B')]); await navigation
  })
  it('keeps a committed mutation successful when its current refresh fails', async () => {
    vi.mocked(cardsApi.getCards).mockRejectedValue(new Error('offline after commit'))
    const { store, pending } = start()
    await expect(pending).resolves.not.toThrow()
    expect(store.currentBoardCards.some(c => c.id === 'parent')).toBe(false)
    expect(warning).toHaveBeenCalledWith(expect.stringContaining('saved'))
    expect(error).not.toHaveBeenCalled(); expect(store.loading).toBe(false)
  })
  it('retains the current authorization error without rejecting the saved action', async () => {
    vi.mocked(cardsApi.getCards).mockRejectedValue({ response: { status: 403 } })
    const { store, pending } = start(); await pending
    expect(store.error).toBe('You no longer have access to this board')
    expect(error).toHaveBeenCalledWith('You no longer have access to this board')
    expect(warning).not.toHaveBeenCalled()
  })
  it.each(['background', 'explicit', 'invalidated'] as const)('carries the saved-action warning through a joined or queued %s read', async (intent) => {
    const first = deferred<Card[]>()
    vi.mocked(cardsApi.getCards).mockReturnValueOnce(first.promise).mockRejectedValue(new Error('queued refresh offline'))
    const store = useBoardStore(); store.currentBoard = board('A'); store.currentBoardCards = [card('parent', 'A'), card('child', 'A', 'parent')]
    const existing = store.fetchBoard('A', { intent: intent === 'invalidated' ? 'background' : intent }); await flushPromises()
    const pending = action === 'archive' ? store.setCardArchived('A', 'parent', true, '2026-01-01T00:00:00Z') : store.deleteCard('A', 'parent')
    await flushPromises()
    if (intent === 'background') first.reject(new Error('joined refresh offline'))
    else first.resolve([card('parent', 'A'), card('child', 'A', 'parent')])
    await existing; await pending; await flushPromises()
    expect(warning).toHaveBeenCalledWith(expect.stringContaining('saved'))
    expect(error).not.toHaveBeenCalled()
  })
  it('uses the guarded detail refresh to install detached children and counts', async () => {
    vi.mocked(cardsApi.getCards).mockResolvedValue([card('child', 'A')])
    const { store, pending } = start(); await pending
    expect(boardsApi.getBoard).toHaveBeenCalledWith('A', expect.objectContaining({ skipRetry: true, signal: expect.any(AbortSignal) }))
    expect(store.currentBoardCards[0]?.parentCardId).toBeNull()
    expect(store.currentBoard?.columns[0]?.cardCount).toBe(1)
  })
 })
}
