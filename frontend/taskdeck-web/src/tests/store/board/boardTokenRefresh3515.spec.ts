import { createBoardState } from '../../../store/board/boardState'
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest'
import { ref } from 'vue'
import { clearAll, setSession, setToken } from '../../../utils/tokenStorage'

const { mockBoardsApi } = vi.hoisted(() => ({
  mockBoardsApi: {
    getBoards: vi.fn(),
    getBoard: vi.fn(),
    createBoard: vi.fn(),
    updateBoard: vi.fn(),
    deleteBoard: vi.fn(),
  },
}))

const { mockCardsApi } = vi.hoisted(() => ({
  mockCardsApi: {
    getCards: vi.fn(),
  },
}))

const { mockLabelsApi } = vi.hoisted(() => ({
  mockLabelsApi: {
    getLabels: vi.fn(),
  },
}))

vi.mock('../../../api/boardsApi', () => ({
  boardsApi: mockBoardsApi,
}))

vi.mock('../../../api/cardsApi', () => ({
  cardsApi: mockCardsApi,
}))

vi.mock('../../../api/labelsApi', () => ({
  labelsApi: mockLabelsApi,
}))

import { createBoardCrudActions } from '../../../store/board/boardCrudStore'
import type { CardFilters } from '../../../store/board/boardState'

// Structurally valid JWTs (empty-object payload) so tokenStorage accepts them.
const firstToken = 'eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJmaXJzdCJ9.synthetic'
const secondToken = 'eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJzZWNvbmQifQ.synthetic'

function signInAs(userId: string, token: string = firstToken) {
  expect(setToken(token)).toBe(true)
  expect(
    setSession({ userId, username: `${userId}-name`, email: `${userId}@example.test` }),
  ).toBe(true)
}

function createMockState() {
  return {
    ...createBoardState(),
    boards: ref([
      { id: 'board-1', name: 'My Board' },
      { id: 'board-2', name: 'Other' },
    ]),
    currentBoard: ref<{ id: string; name: string } | null>(null),
    currentBoardRequestGeneration: ref(0),
    currentBoardPayloadGeneration: ref(0),
    currentBoardCards: ref<Array<{ id: string }>>([]),
    currentBoardLabels: ref<Array<{ id: string }>>([]),
    cardCommentsByCardId: ref<Record<string, unknown>>({}),
    boardPresenceMembers: ref<Array<{ id: string }>>([]),
    editingCardId: ref<string | null>(null),
    activeBoardId: ref<string | null>('board-1'),
    loading: ref(false),
    error: ref<string | null>(null),
    filters: ref<CardFilters>({
      searchText: '',
      labelIds: [],
      dueDateFilter: 'all',
      showBlockedOnly: false,
    }),
  }
}

function createMockHelpers() {
  const boardDetailMutationEpochs = new Map<string, number>()

  return {
    guardDemoMutation: vi.fn(),
    handleApiError: vi.fn((_err: unknown, fallback: string) => fallback),
    isDemoMode: false,
    toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn(), info: vi.fn() },
    getBoardDetailMutationEpoch: vi.fn(
      (boardId: string) => boardDetailMutationEpochs.get(boardId) ?? 0,
    ),
    markBoardDetailMutation: vi.fn((boardId: string) => {
      boardDetailMutationEpochs.set(boardId, (boardDetailMutationEpochs.get(boardId) ?? 0) + 1)
    }),
  }
}

function createDeferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((innerResolve, innerReject) => {
    resolve = innerResolve
    reject = innerReject
  })
  return { promise, resolve, reject }
}

describe('board token-refresh revocation (#3515)', () => {
  let state: ReturnType<typeof createMockState>
  let helpers: ReturnType<typeof createMockHelpers>

  beforeEach(() => {
    vi.clearAllMocks()
    mockBoardsApi.getBoard.mockReset()
    mockCardsApi.getCards.mockReset()
    mockLabelsApi.getLabels.mockReset()
    localStorage.removeItem('taskdeck_token')
    localStorage.removeItem('taskdeck_session')
    state = createMockState()
    helpers = createMockHelpers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('notifies the route when a background 403 settles after a same-user token refresh', async () => {
    signInAs('user-a', firstToken)
    const pending = createDeferred<{ id: string; name: string; columns: [] }>()
    mockBoardsApi.getBoard.mockReturnValueOnce(pending.promise)
    mockCardsApi.getCards.mockResolvedValueOnce([])
    mockLabelsApi.getLabels.mockResolvedValueOnce([])
    state.currentBoard.value = { id: 'board-1', name: 'Cached board' }
    state.currentBoardCards.value = [{ id: 'cached-card' }]
    const { fetchBoard } = createBoardCrudActions(state as any, helpers as any)
    const onBackgroundForbidden = vi.fn()
    const read = fetchBoard('board-1', { intent: 'background', onBackgroundForbidden })

    // Same-user refresh: new token, same user, no credential removal.
    signInAs('user-a', secondToken)
    pending.reject({ response: { status: 403 } })
    await expect(read).resolves.toBe(false)

    expect(onBackgroundForbidden).toHaveBeenCalledExactlyOnceWith('board-1')
    // Single-notice contract: the route owns the persistent revocation notice,
    // so the store writes neither shared error nor toast for a callback 403.
    expect(helpers.handleApiError).not.toHaveBeenCalled()
    expect(state.error.value).toBeNull()
    expect(state.currentBoard.value).toEqual({ id: 'board-1', name: 'Cached board' })
    expect(state.currentBoardCards.value).toEqual([{ id: 'cached-card' }])
  })

  it('suppresses a background 403 that settles after logout', async () => {
    signInAs('user-a', firstToken)
    const pending = createDeferred<{ id: string; name: string; columns: [] }>()
    mockBoardsApi.getBoard.mockReturnValueOnce(pending.promise)
    mockCardsApi.getCards.mockResolvedValueOnce([])
    mockLabelsApi.getLabels.mockResolvedValueOnce([])
    const { fetchBoard } = createBoardCrudActions(state as any, helpers as any)
    const onBackgroundForbidden = vi.fn()
    const read = fetchBoard('board-1', { intent: 'background', onBackgroundForbidden })

    clearAll()
    pending.reject({ response: { status: 403 } })
    await expect(read).resolves.toBe(false)

    expect(onBackgroundForbidden).not.toHaveBeenCalled()
    expect(helpers.handleApiError).not.toHaveBeenCalled()
    expect(state.error.value).toBeNull()
  })

  it('does not notify after logout followed by same-user re-login', async () => {
    signInAs('user-a', firstToken)
    const pending = createDeferred<{ id: string; name: string; columns: [] }>()
    mockBoardsApi.getBoard.mockReturnValueOnce(pending.promise)
    mockCardsApi.getCards.mockResolvedValueOnce([])
    mockLabelsApi.getLabels.mockResolvedValueOnce([])
    const { fetchBoard } = createBoardCrudActions(state as any, helpers as any)
    const onBackgroundForbidden = vi.fn()
    const read = fetchBoard('board-1', { intent: 'background', onBackgroundForbidden })

    // Same user, but the logout advanced the session break: still stale.
    clearAll()
    signInAs('user-a', secondToken)
    pending.reject({ response: { status: 403 } })
    await expect(read).resolves.toBe(false)

    expect(onBackgroundForbidden).not.toHaveBeenCalled()
    expect(helpers.handleApiError).not.toHaveBeenCalled()
    expect(state.error.value).toBeNull()
  })

  it('does not notify when a different user replaces the session mid-read', async () => {
    signInAs('user-a', firstToken)
    const pending = createDeferred<{ id: string; name: string; columns: [] }>()
    mockBoardsApi.getBoard.mockReturnValueOnce(pending.promise)
    mockCardsApi.getCards.mockResolvedValueOnce([])
    mockLabelsApi.getLabels.mockResolvedValueOnce([])
    const { fetchBoard } = createBoardCrudActions(state as any, helpers as any)
    const onBackgroundForbidden = vi.fn()
    const read = fetchBoard('board-1', { intent: 'background', onBackgroundForbidden })

    signInAs('user-b', secondToken)
    pending.reject({ response: { status: 403 } })
    await expect(read).resolves.toBe(false)

    expect(onBackgroundForbidden).not.toHaveBeenCalled()
    expect(helpers.handleApiError).not.toHaveBeenCalled()
    expect(state.error.value).toBeNull()
  })

  it('drains a queued refresh across a same-user token refresh and notifies on its 403', async () => {
    signInAs('user-a', firstToken)
    const explicitBoard = createDeferred<{ id: string; name: string; columns: [] }>()
    mockBoardsApi.getBoard
      .mockReturnValueOnce(explicitBoard.promise)
      .mockRejectedValueOnce({ response: { status: 403 } })
    mockCardsApi.getCards.mockResolvedValue([])
    mockLabelsApi.getLabels.mockResolvedValue([])
    const { fetchBoard } = createBoardCrudActions(state as any, helpers as any)
    const onBackgroundForbidden = vi.fn()
    const explicit = fetchBoard('board-1')
    const queued = fetchBoard('board-1', { intent: 'background', onBackgroundForbidden })

    signInAs('user-a', secondToken)
    explicitBoard.resolve({ id: 'board-1', name: 'Current board', columns: [] })
    await expect(explicit).resolves.toBe(true)
    await expect(queued).resolves.toBe(false)

    expect(onBackgroundForbidden).toHaveBeenCalledExactlyOnceWith('board-1')
    expect(helpers.handleApiError).not.toHaveBeenCalled()
    expect(state.error.value).toBeNull()
  })

  it('discards a queued refresh when a new user replaces the session before the drain', async () => {
    signInAs('user-a', firstToken)
    const explicitBoard = createDeferred<{ id: string; name: string; columns: [] }>()
    mockBoardsApi.getBoard.mockReturnValueOnce(explicitBoard.promise)
    mockCardsApi.getCards.mockResolvedValue([])
    mockLabelsApi.getLabels.mockResolvedValue([])
    const { fetchBoard } = createBoardCrudActions(state as any, helpers as any)
    const onBackgroundForbidden = vi.fn()
    const explicit = fetchBoard('board-1')
    const queued = fetchBoard('board-1', { intent: 'background', onBackgroundForbidden })

    signInAs('user-b', secondToken)
    explicitBoard.resolve({ id: 'board-1', name: 'Current board', columns: [] })
    await expect(explicit).resolves.toBe(true)
    await expect(queued).resolves.toBe(false)

    // No second request: the stale queue never ran on the new credentials.
    expect(mockBoardsApi.getBoard).toHaveBeenCalledTimes(1)
    expect(onBackgroundForbidden).not.toHaveBeenCalled()
    expect(helpers.handleApiError).not.toHaveBeenCalled()
    expect(state.error.value).toBeNull()
  })

  it('surfaces a current background 403 through the store when no view callback exists', async () => {
    signInAs('user-a', firstToken)
    helpers.handleApiError.mockImplementationOnce((_error: unknown, fallback: string) => {
      state.error.value = fallback
      return fallback
    })
    mockBoardsApi.getBoard.mockRejectedValueOnce({ response: { status: 403 } })
    mockCardsApi.getCards.mockResolvedValueOnce([])
    mockLabelsApi.getLabels.mockResolvedValueOnce([])
    const { fetchBoard } = createBoardCrudActions(state as any, helpers as any)

    await expect(fetchBoard('board-1', { intent: 'background' })).resolves.toBe(false)

    expect(helpers.handleApiError).toHaveBeenCalledWith(
      expect.objectContaining({ message: 'You no longer have access to this board' }),
      'You no longer have access to this board',
    )
    expect(state.error.value).toBe('You no longer have access to this board')
  })

  it('still reports a transient background failure after a same-user refresh', async () => {
    signInAs('user-a', firstToken)
    mockBoardsApi.getBoard.mockRejectedValueOnce(new Error('socket hung up'))
    mockCardsApi.getCards.mockResolvedValueOnce([])
    mockLabelsApi.getLabels.mockResolvedValueOnce([])
    const { fetchBoard } = createBoardCrudActions(state as any, helpers as any)
    const onBackgroundForbidden = vi.fn()
    const read = fetchBoard('board-1', {
      intent: 'background',
      backgroundFailureMessage: 'Card change saved, but the board could not be refreshed.',
      onBackgroundForbidden,
    })

    signInAs('user-a', secondToken)
    await expect(read).resolves.toBe(false)

    expect(onBackgroundForbidden).not.toHaveBeenCalled()
    expect(helpers.toast.warning).toHaveBeenCalledWith(
      'Card change saved, but the board could not be refreshed.',
    )
  })
})
