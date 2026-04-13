/**
 * Additional resilience tests for frontend: slow API responses, duplicate request
 * prevention, and localStorage corruption/clearing mid-session.
 * Issue #720 (TST-67): Covers slow API (5+ seconds) → loading indicators, no
 * duplicate requests; and localStorage corrupted/cleared → graceful handling.
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useBoardStore } from '../../store/boardStore'
import { useCaptureStore } from '../../store/captureStore'
import { useSessionStore } from '../../store/sessionStore'
import { boardsApi } from '../../api/boardsApi'
import { captureApi } from '../../api/captureApi'
import { authApi } from '../../api/authApi'
import * as tokenStorage from '../../utils/tokenStorage'

// ─── global mocks ────────────────────────────────────────────────────────────

const toastMocks = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn(), info: vi.fn() }))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => toastMocks,
}))

vi.mock('../../composables/useErrorMapper', () => ({
  getErrorDisplay: (_err: unknown, fallback: string) => ({ message: fallback, code: null }),
}))

vi.mock('../../api/boardsApi', () => ({
  boardsApi: {
    getBoards: vi.fn(),
    getBoard: vi.fn(),
    createBoard: vi.fn(),
    updateBoard: vi.fn(),
    deleteBoard: vi.fn(),
  },
}))

vi.mock('../../api/cardsApi', () => ({
  cardsApi: {
    getCards: vi.fn(),
    createCard: vi.fn(),
    updateCard: vi.fn(),
    moveCard: vi.fn(),
    deleteCard: vi.fn(),
  },
}))

vi.mock('../../api/cardCommentsApi', () => ({
  cardCommentsApi: {
    getComments: vi.fn(),
    createComment: vi.fn(),
    updateComment: vi.fn(),
    deleteComment: vi.fn(),
  },
}))

vi.mock('../../api/columnsApi', () => ({
  columnsApi: {
    createColumn: vi.fn(),
    updateColumn: vi.fn(),
    deleteColumn: vi.fn(),
  },
}))

vi.mock('../../api/labelsApi', () => ({
  labelsApi: {
    getLabels: vi.fn(),
    createLabel: vi.fn(),
    updateLabel: vi.fn(),
    deleteLabel: vi.fn(),
  },
}))

vi.mock('../../api/captureApi', () => ({
  captureApi: {
    createItem: vi.fn(),
    listItems: vi.fn(),
    getItem: vi.fn(),
    ignoreItem: vi.fn(),
    cancelItem: vi.fn(),
    enqueueTriage: vi.fn(),
    batchTriage: vi.fn(),
    updateSuggestion: vi.fn(),
  },
}))

vi.mock('../../api/authApi', () => ({
  authApi: {
    login: vi.fn(),
    register: vi.fn(),
    changePassword: vi.fn(),
    getProviders: vi.fn(),
    exchangeOAuthCode: vi.fn(),
  },
}))

vi.mock('../../api/usersApi', () => ({
  usersApi: {
    getUser: vi.fn(),
  },
}))

// ─── helpers ─────────────────────────────────────────────────────────────────

function makeNetworkError(message = 'Network Error'): Error {
  return Object.assign(new Error(message), { code: 'ERR_NETWORK' })
}

/**
 * Creates a promise that resolves after the specified delay, simulating a slow API.
 * Uses vi.advanceTimersByTimeAsync for deterministic timer control.
 */
function makeSlowResponse<T>(value: T, delayMs: number): Promise<T> {
  return new Promise((resolve) => {
    setTimeout(() => resolve(value), delayMs)
  })
}

// ─── boardStore — slow API resilience ────────────────────────────────────────

describe('boardStore — slow API response handling', () => {
  let store: ReturnType<typeof useBoardStore>

  beforeEach(() => {
    setActivePinia(createPinia())
    store = useBoardStore()
    vi.clearAllMocks()
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('sets loading=true during a slow API call and clears it on completion', async () => {
    const boards = [
      {
        id: 'b1',
        name: 'Board',
        description: '',
        isArchived: false,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
        columns: [],
      },
    ]
    // Simulate a 5-second API response.
    vi.mocked(boardsApi.getBoards).mockReturnValue(makeSlowResponse(boards, 5000))

    const fetchPromise = store.fetchBoards()

    // Immediately after starting, loading should be true.
    expect(store.loading).toBe(true)

    // Advance past the 5-second delay.
    await vi.advanceTimersByTimeAsync(5000)
    await fetchPromise

    expect(store.loading).toBe(false)
    expect(store.boards).toHaveLength(1)
  })

  it('does not fire duplicate requests during throttle window', async () => {
    const boards = [
      {
        id: 'b1',
        name: 'Board',
        description: '',
        isArchived: false,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
        columns: [],
      },
    ]
    vi.mocked(boardsApi.getBoards).mockResolvedValue(boards)

    // First fetch — should hit API.
    await store.fetchBoards()
    expect(boardsApi.getBoards).toHaveBeenCalledTimes(1)

    // Second fetch within throttle window — should be skipped.
    await store.fetchBoards()
    expect(boardsApi.getBoards).toHaveBeenCalledTimes(1)

    // Advance past throttle window (5 seconds).
    await vi.advanceTimersByTimeAsync(5001)

    // Third fetch after throttle expires — should hit API again.
    await store.fetchBoards()
    expect(boardsApi.getBoards).toHaveBeenCalledTimes(2)
  })

  it('error state is set when slow API eventually fails', async () => {
    vi.mocked(boardsApi.getBoards).mockReturnValue(
      makeSlowResponse(null, 5000).then(() => {
        throw makeNetworkError('Timeout after 5s')
      }),
    )

    const fetchPromise = store.fetchBoards()
    expect(store.loading).toBe(true)

    await vi.advanceTimersByTimeAsync(5000)

    await expect(fetchPromise).rejects.toThrow()

    expect(store.loading).toBe(false)
    expect(store.error).toBeTruthy()
  })
})

// ─── captureStore — slow API resilience ──────────────────────────────────────

describe('captureStore — slow API response handling', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('handles slow createItem without crashing; shows toast on error after delay', async () => {
    vi.mocked(captureApi.createItem).mockReturnValue(
      makeSlowResponse(null, 6000).then(() => {
        throw makeNetworkError('Slow timeout')
      }),
    )

    const store = useCaptureStore()
    const createPromise = store.createItem({ text: 'Slow capture', boardId: null })

    await vi.advanceTimersByTimeAsync(6000)

    try {
      await createPromise
    } catch {
      // Expected failure
    }

    expect(toastMocks.error).toHaveBeenCalled()
  })
})

// ─── sessionStore — localStorage corruption and clearing ─────────────────────

describe('sessionStore — localStorage corruption mid-session', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    localStorage.clear()
  })

  it('gracefully handles localStorage cleared mid-session via restoreSession', () => {
    const store = useSessionStore()

    // Simulate a valid session being set (mock the login flow).
    // We can't actually call login since the API is mocked, so we
    // test restoreSession which is the path that runs on app init.

    // First, seed localStorage with a previously valid session.
    // Use a structurally valid but expired-like JWT.
    localStorage.setItem('taskdeck_token', 'not-a-valid-jwt')
    localStorage.setItem('taskdeck_session', JSON.stringify({
      userId: 'u1',
      username: 'testuser',
      email: 'test@example.com',
    }))

    // restoreSession should detect the invalid token and clean up.
    store.restoreSession()

    expect(store.isAuthenticated).toBe(false)
    expect(store.token).toBeFalsy()
    expect(localStorage.getItem('taskdeck_token')).toBeNull()
  })

  it('handles corrupted JSON in localStorage session without throwing', () => {
    localStorage.setItem('taskdeck_session', '{corrupted json!!!')

    const act = () => {
      setActivePinia(createPinia())
      const store = useSessionStore()
      store.restoreSession()
    }

    expect(act).not.toThrow()
    // The session module should have cleaned up the corrupted data.
    expect(localStorage.getItem('taskdeck_session')).toBeNull()
  })

  it('handles localStorage suddenly cleared after session was established', () => {
    const store = useSessionStore()

    // Call restoreSession on empty localStorage — no crash.
    store.restoreSession()

    expect(store.isAuthenticated).toBe(false)
    expect(store.userId).toBeNull()
  })

  it('tokenStorage.getToken returns null and cleans up for corrupted token', () => {
    localStorage.setItem('taskdeck_token', 'definitely-not-jwt-format')

    const result = tokenStorage.getToken()

    expect(result).toBeNull()
    expect(localStorage.getItem('taskdeck_token')).toBeNull()
  })

  it('tokenStorage.getSession returns null and cleans up for corrupted session', () => {
    localStorage.setItem('taskdeck_session', 'not-even-json')

    const result = tokenStorage.getSession()

    expect(result).toBeNull()
    expect(localStorage.getItem('taskdeck_session')).toBeNull()
  })

  it('tokenStorage.getSession returns null for session with missing required fields', () => {
    localStorage.setItem('taskdeck_session', JSON.stringify({ userId: 'u1' }))

    const result = tokenStorage.getSession()

    expect(result).toBeNull()
  })

  it('tokenStorage.setToken rejects and returns false for non-JWT strings', () => {
    const result = tokenStorage.setToken('bad-token')

    expect(result).toBe(false)
    expect(localStorage.getItem('taskdeck_token')).toBeNull()
  })
})

// ─── boardStore — loading state consistency under concurrent operations ──────

describe('boardStore — loading state consistency', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('loading is false initially and after a completed fetch', async () => {
    const store = useBoardStore()
    expect(store.loading).toBe(false)

    vi.mocked(boardsApi.getBoards).mockResolvedValue([])
    await store.fetchBoards()

    expect(store.loading).toBe(false)
  })

  it('loading returns to false after a failed fetch', async () => {
    const store = useBoardStore()
    vi.mocked(boardsApi.getBoards).mockRejectedValue(makeNetworkError())

    await expect(store.fetchBoards()).rejects.toThrow()

    expect(store.loading).toBe(false)
  })

  it('error is cleared on a subsequent successful fetch', async () => {
    const store = useBoardStore()

    // First: fail
    vi.mocked(boardsApi.getBoards).mockRejectedValueOnce(makeNetworkError())
    await expect(store.fetchBoards()).rejects.toThrow()
    expect(store.error).toBeTruthy()

    // Second: succeed (use filter to bypass throttle)
    vi.mocked(boardsApi.getBoards).mockResolvedValueOnce([])
    await store.fetchBoards('bypass-throttle')
    expect(store.error).toBeNull()
  })
})
