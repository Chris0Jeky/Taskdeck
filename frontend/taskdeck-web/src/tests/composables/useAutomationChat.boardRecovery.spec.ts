import { beforeEach, describe, expect, it, vi } from 'vitest'
import { BOARD_REQUEST_TIMEOUT_MS } from '../../api/http'

const routerMocks = vi.hoisted(() => ({ push: vi.fn() }))
const routeMocks = vi.hoisted(() => ({ query: {} as Record<string, string | undefined> }))
const toastMocks = vi.hoisted(() => ({ error: vi.fn() }))
const chatApiMocks = vi.hoisted(() => ({
  getMySessions: vi.fn().mockResolvedValue([]),
  getSession: vi.fn(),
  createSession: vi.fn(),
  sendMessage: vi.fn(),
  bindBoard: vi.fn(),
  getHealth: vi.fn().mockResolvedValue({ status: 'healthy' }),
}))
const boardsApiMocks = vi.hoisted(() => ({ getBoards: vi.fn() }))

vi.mock('vue-router', () => ({ useRouter: () => routerMocks, useRoute: () => routeMocks }))
vi.mock('../../store/toastStore', () => ({ useToastStore: () => toastMocks }))
vi.mock('../../api/chatApi', () => ({ chatApi: chatApiMocks }))
vi.mock('../../api/boardsApi', () => ({ boardsApi: boardsApiMocks }))
vi.mock('vue', async (importOriginal) => {
  const actual = await importOriginal<typeof import('vue')>()
  return {
    ...actual,
    onMounted: (fn: () => void) => fn(),
    watch: vi.fn().mockReturnValue(vi.fn()),
    onScopeDispose: vi.fn(),
  }
})

async function loadComposable() {
  vi.resetModules()
  return import('../../composables/useAutomationChat')
}

describe('useAutomationChat board-option recovery (#2689)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    routeMocks.query = {}
    chatApiMocks.getMySessions.mockResolvedValue([])
    chatApiMocks.getHealth.mockResolvedValue({ status: 'healthy' })
  })

  it('uses one bounded attempt for mount and user retry, then clears the failure', async () => {
    boardsApiMocks.getBoards
      .mockRejectedValueOnce(new Error('Boards unavailable'))
      .mockResolvedValueOnce([
        { id: 'board-1', name: 'Recovered Board', description: null, isArchived: false, canWrite: true },
      ])

    const { useAutomationChat } = await loadComposable()
    const chat = useAutomationChat()
    const boundedRead = { timeout: BOARD_REQUEST_TIMEOUT_MS, skipRetry: true }

    await vi.waitFor(() => {
      expect(chat.loadingBoards.value).toBe(false)
      expect(chat.boardOptionsLoadError.value).toBe('Boards unavailable')
    })
    expect(boardsApiMocks.getBoards).toHaveBeenNthCalledWith(1, undefined, false, boundedRead)

    await chat.loadBoardOptions()

    expect(boardsApiMocks.getBoards).toHaveBeenNthCalledWith(2, undefined, false, boundedRead)
    expect(chat.boardOptionsLoadError.value).toBeNull()
    expect(chat.boardOptions.value.map((option) => option.label)).toEqual(['Recovered Board'])
  })
})
