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

vi.mock('vue', async (importOriginal) => {
  const actual = await importOriginal<typeof import('vue')>()
  return {
    ...actual,
    onMounted: (fn: () => void) => fn(),
    watch: vi.fn(),
  }
})

async function loadComposable() {
  vi.resetModules()
  return import('../../composables/useAutomationChat')
}

describe('useAutomationChat', () => {
  beforeEach(() => {
    vi.clearAllMocks()
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
})
