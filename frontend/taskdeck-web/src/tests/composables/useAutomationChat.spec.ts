import { describe, expect, it, beforeEach, vi } from 'vitest'

const routerMocks = vi.hoisted(() => ({
  push: vi.fn(),
}))

const routeMocks = vi.hoisted(() => ({
  query: {} as Record<string, string | undefined>,
}))

const toastMocks = vi.hoisted(() => ({
  error: vi.fn(),
}))

const chatApiMocks = vi.hoisted(() => ({
  getMySessions: vi.fn().mockResolvedValue([]),
  getSession: vi.fn(),
  createSession: vi.fn(),
  sendMessage: vi.fn(),
  getHealth: vi.fn().mockResolvedValue({ status: 'healthy' }),
}))

const boardsApiMocks = vi.hoisted(() => ({
  getBoards: vi.fn().mockResolvedValue([]),
}))

vi.mock('vue-router', () => ({
  useRouter: () => routerMocks,
  useRoute: () => routeMocks,
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => toastMocks,
}))

vi.mock('../../api/chatApi', () => ({
  chatApi: chatApiMocks,
}))

vi.mock('../../api/boardsApi', () => ({
  boardsApi: boardsApiMocks,
}))

const scopeDisposeFns: Array<() => void> = []

vi.mock('vue', async (importOriginal) => {
  const actual = await importOriginal<typeof import('vue')>()
  return {
    ...actual,
    onMounted: (fn: () => void) => fn(),
    watch: vi.fn().mockReturnValue(vi.fn()),
    onScopeDispose: (fn: () => void) => { scopeDisposeFns.push(fn) },
  }
})

async function loadComposable() {
  vi.resetModules()
  return import('../../composables/useAutomationChat')
}

describe('useAutomationChat', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    scopeDisposeFns.length = 0
    routeMocks.query = {}
    chatApiMocks.getMySessions.mockResolvedValue([])
    chatApiMocks.getSession.mockResolvedValue(undefined)
    chatApiMocks.createSession.mockResolvedValue(undefined)
    chatApiMocks.sendMessage.mockResolvedValue(undefined)
    chatApiMocks.getHealth.mockResolvedValue({ status: 'healthy' })
    boardsApiMocks.getBoards.mockResolvedValue([])
  })

  describe('initial state', () => {
    it('starts with empty sessions and no selected session', async () => {
      const { useAutomationChat } = await loadComposable()
      const chat = useAutomationChat()

      await vi.waitFor(() => {
        expect(chat.sessions.value).toEqual([])
      })
      expect(chat.selectedSession.value).toBeNull()
      expect(chat.messageContent.value).toBe('')
      expect(chat.requestProposal.value).toBe(false)
    })

    it('loads sessions and provider health on mount', async () => {
      const { useAutomationChat } = await loadComposable()
      useAutomationChat()

      await vi.waitFor(() => {
        expect(chatApiMocks.getMySessions).toHaveBeenCalledTimes(1)
        expect(chatApiMocks.getHealth).toHaveBeenCalledTimes(1)
      })
    })
  })

  describe('sortedMessages', () => {
    it('returns empty when no session selected', async () => {
      const { useAutomationChat } = await loadComposable()
      const chat = useAutomationChat()

      expect(chat.sortedMessages.value).toEqual([])
    })

    it('sorts messages by createdAt', async () => {
      chatApiMocks.getMySessions.mockResolvedValue([
        {
          id: 's1',
          title: 'Test',
          boardId: null,
          recentMessages: [
            { id: 'm2', content: 'second', role: 0, messageType: 'chat', createdAt: '2026-05-16T10:01:00Z' },
            { id: 'm1', content: 'first', role: 0, messageType: 'chat', createdAt: '2026-05-16T10:00:00Z' },
          ],
        },
      ])
      chatApiMocks.getSession.mockResolvedValue({
        id: 's1',
        title: 'Test',
        boardId: null,
        recentMessages: [
          { id: 'm2', content: 'second', role: 0, messageType: 'chat', createdAt: '2026-05-16T10:01:00Z' },
          { id: 'm1', content: 'first', role: 0, messageType: 'chat', createdAt: '2026-05-16T10:00:00Z' },
        ],
      })

      const { useAutomationChat } = await loadComposable()
      const chat = useAutomationChat()

      await vi.waitFor(() => {
        expect(chat.selectedSession.value).not.toBeNull()
      })
      expect(chat.sortedMessages.value[0].content).toBe('first')
      expect(chat.sortedMessages.value[1].content).toBe('second')
    })
  })

  describe('lastMessageIsClarification', () => {
    it('returns false when no messages', async () => {
      const { useAutomationChat } = await loadComposable()
      const chat = useAutomationChat()

      expect(chat.lastMessageIsClarification.value).toBe(false)
    })

    it('returns true when last message is assistant clarification', async () => {
      chatApiMocks.getMySessions.mockResolvedValue([
        {
          id: 's1',
          title: 'Test',
          boardId: null,
          recentMessages: [
            { id: 'm1', content: 'What do you mean?', role: 1, messageType: 'clarification', createdAt: '2026-05-16T10:00:00Z' },
          ],
        },
      ])
      chatApiMocks.getSession.mockResolvedValue({
        id: 's1',
        title: 'Test',
        boardId: null,
        recentMessages: [
          { id: 'm1', content: 'What do you mean?', role: 1, messageType: 'clarification', createdAt: '2026-05-16T10:00:00Z' },
        ],
      })

      const { useAutomationChat } = await loadComposable()
      const chat = useAutomationChat()

      await vi.waitFor(() => {
        expect(chat.selectedSession.value).not.toBeNull()
      })
      expect(chat.lastMessageIsClarification.value).toBe(true)
    })
  })

  describe('handleCreateSession', () => {
    it('shows error toast when title is empty', async () => {
      const { useAutomationChat } = await loadComposable()
      const chat = useAutomationChat()

      chat.newSessionTitle.value = '   '
      await chat.handleCreateSession()

      expect(toastMocks.error).toHaveBeenCalledWith('Session title is required')
      expect(chatApiMocks.createSession).not.toHaveBeenCalled()
    })

    it('creates session when title provided without board', async () => {
      chatApiMocks.createSession.mockResolvedValue({ id: 'new-session' })
      chatApiMocks.getSession.mockResolvedValue({
        id: 'new-session',
        title: 'My Session',
        boardId: null,
        recentMessages: [],
      })

      const { useAutomationChat } = await loadComposable()
      const chat = useAutomationChat()

      chat.newSessionTitle.value = 'My Session'
      await chat.handleCreateSession()

      expect(chatApiMocks.createSession).toHaveBeenCalledWith({
        title: 'My Session',
        boardId: null,
      })
    })

    it('shows error when board name does not resolve', async () => {
      boardsApiMocks.getBoards.mockResolvedValue([
        { id: 'b1', name: 'Project Alpha', description: null, isArchived: false },
      ])

      const { useAutomationChat } = await loadComposable()
      const chat = useAutomationChat()

      await vi.waitFor(() => {
        expect(boardsApiMocks.getBoards).toHaveBeenCalled()
      })

      chat.newSessionTitle.value = 'Test Session'
      chat.newSessionBoardId.value = 'nonexistent board'
      await chat.handleCreateSession()

      expect(toastMocks.error).toHaveBeenCalledWith(
        'Choose a board from the list or leave board context blank.',
      )
      expect(chatApiMocks.createSession).not.toHaveBeenCalled()
    })
  })

  describe('handleSendMessage', () => {
    it('does nothing when message content is empty', async () => {
      const { useAutomationChat } = await loadComposable()
      const chat = useAutomationChat()

      chat.messageContent.value = ''
      await chat.handleSendMessage()

      expect(chatApiMocks.sendMessage).not.toHaveBeenCalled()
    })

    it('shows error toast when no session is selected', async () => {
      const { useAutomationChat } = await loadComposable()
      const chat = useAutomationChat()

      chat.messageContent.value = 'Hello'
      await chat.handleSendMessage()

      expect(toastMocks.error).toHaveBeenCalledWith('Select a session first')
    })
  })

  describe('applyHintSuggestion', () => {
    it('sets message content and enables requestProposal', async () => {
      const { useAutomationChat } = await loadComposable()
      const chat = useAutomationChat()

      chat.applyHintSuggestion('Move card X to Done')

      expect(chat.messageContent.value).toBe('Move card X to Done')
      expect(chat.requestProposal.value).toBe(true)
    })
  })

  describe('selectedSessionBoardName', () => {
    it('returns "No board context" when session has no board', async () => {
      chatApiMocks.getMySessions.mockResolvedValue([
        { id: 's1', title: 'Test', boardId: null, recentMessages: [] },
      ])
      chatApiMocks.getSession.mockResolvedValue({
        id: 's1',
        title: 'Test',
        boardId: null,
        recentMessages: [],
      })

      const { useAutomationChat } = await loadComposable()
      const chat = useAutomationChat()

      await vi.waitFor(() => {
        expect(chat.selectedSession.value).not.toBeNull()
      })
      expect(chat.selectedSessionBoardName.value).toBe('No board context')
    })

    it('returns board name when board is loaded', async () => {
      boardsApiMocks.getBoards.mockResolvedValue([
        { id: 'b1', name: 'Project Alpha', description: null, isArchived: false },
      ])
      chatApiMocks.getMySessions.mockResolvedValue([
        { id: 's1', title: 'Test', boardId: 'b1', recentMessages: [] },
      ])
      chatApiMocks.getSession.mockResolvedValue({
        id: 's1',
        title: 'Test',
        boardId: 'b1',
        recentMessages: [],
      })

      const { useAutomationChat } = await loadComposable()
      const chat = useAutomationChat()

      await vi.waitFor(() => {
        expect(chat.selectedSession.value).not.toBeNull()
      })
      expect(chat.selectedSessionBoardName.value).toBe('Project Alpha')
    })
  })

  describe('openProposalReview', () => {
    it('navigates to workspace review with proposal hash', async () => {
      chatApiMocks.getMySessions.mockResolvedValue([
        { id: 's1', title: 'Test', boardId: 'b1', recentMessages: [] },
      ])
      chatApiMocks.getSession.mockResolvedValue({
        id: 's1',
        title: 'Test',
        boardId: 'b1',
        recentMessages: [],
      })

      const { useAutomationChat } = await loadComposable()
      const chat = useAutomationChat()

      await vi.waitFor(() => {
        expect(chat.selectedSession.value).not.toBeNull()
      })

      chat.openProposalReview('proposal-abc-123')

      expect(routerMocks.push).toHaveBeenCalledWith({
        name: 'workspace-review',
        query: { boardId: 'b1' },
        hash: '#proposal-proposal-abc-123',
      })
    })
  })

  describe('error handling', () => {
    it('shows toast when loadSessions fails', async () => {
      chatApiMocks.getMySessions.mockRejectedValue(new Error('Network error'))

      const { useAutomationChat } = await loadComposable()
      useAutomationChat()

      await vi.waitFor(() => {
        expect(toastMocks.error).toHaveBeenCalled()
      })
    })

    it('shows toast when loadProviderHealth fails', async () => {
      chatApiMocks.getHealth.mockRejectedValue(new Error('Health check failed'))

      const { useAutomationChat } = await loadComposable()
      const chat = useAutomationChat()

      await vi.waitFor(() => {
        expect(chat.chatHealthLoadError.value).not.toBeNull()
        expect(toastMocks.error).toHaveBeenCalled()
      })
    })
  })

  describe('scope disposal', () => {
    it('registers an onScopeDispose callback', async () => {
      const { useAutomationChat } = await loadComposable()
      useAutomationChat()

      expect(scopeDisposeFns.length).toBeGreaterThan(0)
    })

    it('does not write reactive state after disposal on loadSessions', async () => {
      let resolveGetSessions!: (value: unknown[]) => void
      chatApiMocks.getMySessions.mockReturnValue(
        new Promise((resolve) => { resolveGetSessions = resolve }),
      )

      const { useAutomationChat } = await loadComposable()
      const chat = useAutomationChat()

      // Trigger disposal before the pending request resolves
      for (const fn of scopeDisposeFns) fn()

      // Now resolve the in-flight request
      resolveGetSessions([{ id: 's1', title: 'Late', boardId: null, recentMessages: [] }])
      await vi.waitFor(() => {
        expect(chatApiMocks.getMySessions).toHaveBeenCalled()
      })

      // The sessions ref should NOT have been updated after disposal
      expect(chat.sessions.value).toEqual([])
    })

    it('does not write reactive state after disposal on loadProviderHealth', async () => {
      let resolveHealth!: (value: unknown) => void
      chatApiMocks.getHealth.mockReturnValue(
        new Promise((resolve) => { resolveHealth = resolve }),
      )

      const { useAutomationChat } = await loadComposable()
      const chat = useAutomationChat()

      for (const fn of scopeDisposeFns) fn()

      resolveHealth({ status: 'healthy', provider: 'mock' })
      await vi.waitFor(() => {
        expect(chatApiMocks.getHealth).toHaveBeenCalled()
      })

      expect(chat.chatHealth.value).toBeNull()
    })

    it('does not apply route board context after disposal on the mount continuation', async () => {
      // Route points at a board that the deferred getBoards response contains.
      routeMocks.query = { boardId: 'b1' }

      let resolveBoards!: (value: unknown[]) => void
      const boardsPromise = new Promise<unknown[]>((resolve) => { resolveBoards = resolve })
      boardsApiMocks.getBoards.mockReturnValue(boardsPromise)

      const { useAutomationChat } = await loadComposable()
      const chat = useAutomationChat()

      // Register a disposal hook on the same getBoards promise. Because the
      // composable's internal `await boardsApi.getBoards()` reaction is registered
      // first (during the synchronous onMounted), this runs AFTER availableBoards
      // is populated but BEFORE the onMounted `.then(applyRouteBoardContext)`
      // continuation -- exactly the post-load, post-dispose race the guard covers.
      void boardsPromise.then(() => {
        for (const fn of scopeDisposeFns) fn()
      })

      resolveBoards([{ id: 'b1', name: 'Project Alpha', description: null, isArchived: false }])

      // Drain all microtasks so the deferred continuation runs.
      await new Promise((resolve) => setTimeout(resolve))

      // The continuation must be a no-op after disposal. Without the isDisposed
      // guard, applyRouteBoardContext would resolve 'b1' to 'Project Alpha' and
      // write it to newSessionBoardId after the scope is gone.
      expect(chat.newSessionBoardId.value).toBe('')
    })
  })
})
