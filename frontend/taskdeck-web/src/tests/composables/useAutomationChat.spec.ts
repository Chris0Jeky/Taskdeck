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
  bindBoard: vi.fn(),
  getHealth: vi.fn().mockResolvedValue({ status: 'healthy' }),
}))

function createDeferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void
  const promise = new Promise<T>((innerResolve) => { resolve = innerResolve })
  return { promise, resolve }
}

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
    chatApiMocks.bindBoard.mockResolvedValue(undefined)
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
    it('sets message content for the default action path', async () => {
      const { useAutomationChat } = await loadComposable()
      const chat = useAutomationChat()

      chat.applyHintSuggestion('Move card X to Done')

      expect(chat.messageContent.value).toBe('Move card X to Done')
    })
  })

  describe('board recovery', () => {
    const pendingMessages = [
      { id: 'u1', content: 'create card for release notes', role: 0, messageType: 'text', createdAt: '2026-05-16T10:00:00Z' },
      { id: 'a1', content: 'No board linked', role: 1, messageType: 'action-needs-board', createdAt: '2026-05-16T10:01:00Z' },
    ]

    it('links the existing session and waits for explicit continuation', async () => {
      const unbound = { id: 's1', title: 'Test', boardId: null, recentMessages: pendingMessages }
      const bound = { ...unbound, boardId: 'b1' }
      chatApiMocks.getMySessions.mockResolvedValue([unbound])
      chatApiMocks.getSession.mockResolvedValue(unbound)
      chatApiMocks.bindBoard.mockResolvedValue(bound)
      boardsApiMocks.getBoards.mockResolvedValue([
        { id: 'b1', name: 'Release Board', description: null, isArchived: false, canWrite: true },
      ])

      const { useAutomationChat } = await loadComposable()
      const chat = useAutomationChat()
      await vi.waitFor(() => expect(chat.pendingBoardRecovery.value?.messageId).toBe('a1'))

      await chat.bindBoardToPendingTurn('a1', 'b1')

      expect(chatApiMocks.bindBoard).toHaveBeenCalledWith('s1', { boardId: 'b1' })
      expect(chat.selectedSession.value?.boardId).toBe('b1')
      expect(chat.boardBindingReceipt.value).toBe('Release Board')
      expect(chatApiMocks.sendMessage).not.toHaveBeenCalled()

      await chat.continuePendingInstruction('a1')

      expect(chatApiMocks.sendMessage).toHaveBeenCalledTimes(1)
      expect(chatApiMocks.sendMessage).toHaveBeenCalledWith('s1', {
        content: 'create card for release notes',
      })
    })

    it('offers board recovery for an unbound actionable clarification', async () => {
      const clarificationMessages = [
        pendingMessages[0],
        {
          id: 'a1', role: 1, messageType: 'clarification', createdAt: '2026-05-16T10:01:00Z',
          content: 'Which column should I use? Select a writable board below to keep this instruction and turn it into a proposal you can review.',
        },
      ]
      const session = { id: 's1', title: 'Test', boardId: null, recentMessages: clarificationMessages }
      chatApiMocks.getMySessions.mockResolvedValue([session])
      chatApiMocks.getSession.mockResolvedValue(session)

      const { useAutomationChat } = await loadComposable()
      const chat = useAutomationChat()

      await vi.waitFor(() => expect(chat.pendingBoardRecovery.value?.messageId).toBe('a1'))
    })

    it('consumes a successful continuation when the following refresh fails', async () => {
      const unbound = { id: 's1', title: 'Test', boardId: null, recentMessages: pendingMessages }
      const bound = { ...unbound, boardId: 'b1' }
      const proposal = {
        id: 'a2', sessionId: 's1', role: 1, content: 'Proposal created for review.',
        messageType: 'proposal-reference', proposalId: 'p1', tokenUsage: null,
        createdAt: '2026-05-16T10:02:00Z',
      }
      chatApiMocks.getMySessions.mockResolvedValue([unbound])
      chatApiMocks.getSession
        .mockResolvedValueOnce(unbound)
        .mockRejectedValueOnce(new Error('refresh failed'))
      chatApiMocks.bindBoard.mockResolvedValue(bound)
      chatApiMocks.sendMessage.mockResolvedValue(proposal)
      boardsApiMocks.getBoards.mockResolvedValue([
        { id: 'b1', name: 'Release Board', description: null, isArchived: false, canWrite: true },
      ])

      const { useAutomationChat } = await loadComposable()
      const chat = useAutomationChat()
      await vi.waitFor(() => expect(chat.pendingBoardRecovery.value?.messageId).toBe('a1'))
      await chat.bindBoardToPendingTurn('a1', 'b1')
      await chat.continuePendingInstruction('a1')

      expect(chat.pendingBoardRecovery.value).toBeNull()
      await chat.continuePendingInstruction('a1')
      expect(chatApiMocks.sendMessage).toHaveBeenCalledTimes(1)
    })

    it('clears the pre-bind local fallback when binding returns the authoritative transcript', async () => {
      const instruction = 'create card for release notes'
      const unbound = { id: 's1', title: 'Test', boardId: null, recentMessages: pendingMessages }
      const initialReply = {
        id: 'a2', sessionId: 's1', role: 1, content: 'No board linked for the new instruction',
        messageType: 'action-needs-board', proposalId: null, tokenUsage: 12,
        createdAt: '2026-05-16T10:03:00Z',
      }
      const bound = {
        ...unbound,
        boardId: 'b1',
        recentMessages: [
          { ...pendingMessages[0], id: 'server-user-1' },
          { ...pendingMessages[1], id: 'server-recovery-1' },
        ],
      }
      const proposal = {
        id: 'a3', sessionId: 's1', role: 1, content: 'Proposal created for review.',
        messageType: 'proposal-reference', proposalId: 'p1', tokenUsage: null,
        createdAt: '2026-05-16T10:04:00Z',
      }
      chatApiMocks.getMySessions.mockResolvedValue([unbound])
      chatApiMocks.getSession
        .mockResolvedValueOnce(unbound)
        .mockRejectedValue(new Error('refresh failed'))
      chatApiMocks.sendMessage
        .mockResolvedValueOnce(initialReply)
        .mockResolvedValueOnce(proposal)
      chatApiMocks.bindBoard.mockResolvedValue(bound)
      boardsApiMocks.getBoards.mockResolvedValue([
        { id: 'b1', name: 'Release Board', description: null, isArchived: false, canWrite: true },
      ])

      const { useAutomationChat } = await loadComposable()
      const chat = useAutomationChat()
      await vi.waitFor(() => expect(chat.pendingBoardRecovery.value?.messageId).toBe('a1'))

      chat.messageContent.value = instruction
      await chat.handleSendMessage()
      await vi.waitFor(() => expect(chat.pendingBoardRecovery.value?.messageId).toBe('a2'))
      await chat.bindBoardToPendingTurn('a2', 'b1')
      await chat.continuePendingInstruction('server-recovery-1')

      const visibleMessages = chat.selectedSession.value?.recentMessages ?? []
      expect(chatApiMocks.sendMessage).toHaveBeenCalledTimes(2)
      expect(visibleMessages.filter((message) => message.id.startsWith('local-user-'))).toHaveLength(1)
      expect(visibleMessages.some((message) => message.id === 'local-user-s1-1')).toBe(false)
      expect(visibleMessages.some((message) => message.id === 'a2')).toBe(false)
      expect(visibleMessages.map((message) => message.content)).toEqual([
        instruction,
        'No board linked',
        instruction,
        'Proposal created for review.',
      ])
    })

    it('excludes archived and explicitly read-only boards from binding choices', async () => {
      boardsApiMocks.getBoards.mockResolvedValue([
        { id: 'archived', name: 'Archived', description: null, isArchived: true, canWrite: true },
        { id: 'viewer', name: 'Viewer', description: null, isArchived: false, canWrite: false },
      ])

      const { useAutomationChat } = await loadComposable()
      const chat = useAutomationChat()
      await vi.waitFor(() => expect(boardsApiMocks.getBoards).toHaveBeenCalled())

      expect(chat.eligibleBoards.value).toEqual([])
    })

    it('does not apply a late binding response to another selected session', async () => {
      const first = { id: 's1', title: 'First', boardId: null, recentMessages: pendingMessages }
      const second = { id: 's2', title: 'Second', boardId: null, recentMessages: [] }
      const deferred = createDeferred<{ id: string; title: string; boardId: string; recentMessages: typeof pendingMessages }>()
      chatApiMocks.getMySessions.mockResolvedValue([first, second])
      chatApiMocks.getSession.mockImplementation(async (id: string) => id === 's1' ? first : second)
      chatApiMocks.bindBoard.mockReturnValue(deferred.promise)
      boardsApiMocks.getBoards.mockResolvedValue([
        { id: 'b1', name: 'Release Board', description: null, isArchived: false, canWrite: true },
      ])

      const { useAutomationChat } = await loadComposable()
      const chat = useAutomationChat()
      await vi.waitFor(() => expect(chat.pendingBoardRecovery.value?.messageId).toBe('a1'))

      const pendingBind = chat.bindBoardToPendingTurn('a1', 'b1')
      await chat.loadSession('s2')
      deferred.resolve({ ...first, boardId: 'b1' })
      await pendingBind

      expect(chat.selectedSession.value?.id).toBe('s2')
      expect(chat.selectedSession.value?.boardId).toBeNull()
      expect(chatApiMocks.sendMessage).not.toHaveBeenCalled()
    })

    it('preserves a concurrent send when a delayed bind returns a stale transcript', async () => {
      const session = { id: 's1', title: 'Test', boardId: null, recentMessages: pendingMessages }
      const bound = { ...session, boardId: 'b1', recentMessages: pendingMessages }
      const concurrentReply = {
        id: 'a2', sessionId: 's1', role: 1, messageType: 'action-needs-board',
        proposalId: null, tokenUsage: 12, content: 'No board linked for the concurrent instruction',
        createdAt: '2026-05-16T10:03:00Z',
      }
      const delayedBind = createDeferred<typeof bound>()
      chatApiMocks.getMySessions.mockResolvedValue([session])
      chatApiMocks.getSession
        .mockResolvedValueOnce(session)
        .mockRejectedValueOnce(new Error('refresh failed'))
      chatApiMocks.bindBoard.mockReturnValue(delayedBind.promise)
      chatApiMocks.sendMessage.mockResolvedValue(concurrentReply)
      boardsApiMocks.getBoards.mockResolvedValue([
        { id: 'b1', name: 'Release Board', description: null, isArchived: false, canWrite: true },
      ])

      const { useAutomationChat } = await loadComposable()
      const chat = useAutomationChat()
      await vi.waitFor(() => expect(chat.pendingBoardRecovery.value?.messageId).toBe('a1'))

      const pendingBind = chat.bindBoardToPendingTurn('a1', 'b1')
      chat.messageContent.value = 'concurrent instruction'
      await chat.handleSendMessage()

      delayedBind.resolve(bound)
      await pendingBind

      expect(chat.selectedSession.value?.boardId).toBe('b1')
      expect(chat.selectedSession.value?.recentMessages.map((message) => message.content)).toEqual([
        'create card for release notes',
        'No board linked',
        'concurrent instruction',
        'No board linked for the concurrent instruction',
      ])
    })
  })

  describe('session response races', () => {
    it('does not add a local turn before the send request succeeds', async () => {
      const session = { id: 's1', title: 'First', boardId: null, recentMessages: [] }
      const deferred = createDeferred<{
        id: string; sessionId: string; role: number; messageType: string; proposalId: null;
        tokenUsage: number; content: string; createdAt: string;
      }>()
      chatApiMocks.getMySessions.mockResolvedValue([session])
      chatApiMocks.getSession.mockResolvedValue(session)
      chatApiMocks.sendMessage.mockReturnValue(deferred.promise)

      const { useAutomationChat } = await loadComposable()
      const chat = useAutomationChat()
      await vi.waitFor(() => expect(chat.selectedSession.value?.id).toBe('s1'))

      chat.messageContent.value = 'new instruction'
      const pendingSend = chat.handleSendMessage()
      await Promise.resolve()

      expect(chat.selectedSession.value?.recentMessages).toEqual([])

      deferred.resolve({
        id: 'reply-1', sessionId: 's1', role: 1, messageType: 'text', proposalId: null,
        tokenUsage: 12, content: 'Done', createdAt: '2026-05-16T10:01:00Z',
      })
      await pendingSend
    })

    it('retains the just-submitted instruction when the immediate refresh fails', async () => {
      const oldUser = {
        id: 'old-user', sessionId: 's1', role: 0, messageType: 'text',
        proposalId: null, tokenUsage: null, content: 'older instruction',
        createdAt: '2026-05-16T10:00:00Z',
      }
      const oldRecovery = {
        id: 'old-recovery', sessionId: 's1', role: 1, messageType: 'action-needs-board',
        proposalId: null, tokenUsage: 12, content: 'No board linked',
        createdAt: '2026-05-16T10:01:00Z',
      }
      const session = { id: 's1', title: 'First', boardId: null, recentMessages: [oldUser, oldRecovery] }
      const reply = {
        id: 'new-recovery', sessionId: 's1', role: 1, messageType: 'action-needs-board',
        proposalId: null, tokenUsage: 12, content: 'No board linked for the new instruction',
        createdAt: '2026-05-16T10:03:00Z',
      }
      chatApiMocks.getMySessions.mockResolvedValue([session])
      chatApiMocks.getSession
        .mockResolvedValueOnce(session)
        .mockRejectedValueOnce(new Error('refresh failed'))
      chatApiMocks.sendMessage.mockResolvedValue(reply)

      const { useAutomationChat } = await loadComposable()
      const chat = useAutomationChat()
      await vi.waitFor(() => expect(chat.selectedSession.value?.id).toBe('s1'))

      chat.messageContent.value = 'new instruction'
      await chat.handleSendMessage()

      expect(chatApiMocks.getSession).toHaveBeenNthCalledWith(1, 's1')
      expect(chatApiMocks.getSession).toHaveBeenLastCalledWith('s1', { skipRetry: true })
      expect(chat.sendingMessage.value).toBe(false)
      expect(chat.selectedSession.value?.recentMessages.map((message) => message.content)).toEqual([
        'older instruction',
        'No board linked',
        'new instruction',
        'No board linked for the new instruction',
      ])
      expect(chat.selectedSession.value?.recentMessages[2]?.id).toMatch(/^local-/)
      expect(chat.pendingBoardRecovery.value).toEqual({
        messageId: 'new-recovery',
        instruction: 'new instruction',
      })
    })

    it('replaces retained local messages with the next authoritative session result', async () => {
      const session = { id: 's1', title: 'First', boardId: null, recentMessages: [] }
      const reply = {
        id: 'reply-1', sessionId: 's1', role: 1, messageType: 'text',
        proposalId: null, tokenUsage: 12, content: 'Done',
        createdAt: '2026-05-16T10:01:00Z',
      }
      const authoritative = {
        ...session,
        recentMessages: [
          {
            id: 'server-user-1', sessionId: 's1', role: 0, messageType: 'text',
            proposalId: null, tokenUsage: null, content: 'new instruction',
            createdAt: '2026-05-16T10:00:59Z',
          },
          reply,
        ],
      }
      chatApiMocks.getMySessions.mockResolvedValue([session])
      chatApiMocks.getSession
        .mockResolvedValueOnce(session)
        .mockRejectedValueOnce(new Error('refresh failed'))
        .mockResolvedValueOnce(authoritative)
      chatApiMocks.sendMessage.mockResolvedValue(reply)

      const { useAutomationChat } = await loadComposable()
      const chat = useAutomationChat()
      await vi.waitFor(() => expect(chat.selectedSession.value?.id).toBe('s1'))

      chat.messageContent.value = 'new instruction'
      await chat.handleSendMessage()
      await chat.loadSession('s1')

      expect(chat.selectedSession.value?.recentMessages).toEqual(authoritative.recentMessages)
      expect(chat.selectedSession.value?.recentMessages.filter((message) => message.id === 'reply-1')).toHaveLength(1)
    })

    it('does not switch back when a send response completes after another session is selected', async () => {
      const first = { id: 's1', title: 'First', boardId: null, recentMessages: [] }
      const second = { id: 's2', title: 'Second', boardId: null, recentMessages: [] }
      const deferred = createDeferred<void>()
      chatApiMocks.getMySessions.mockResolvedValue([first, second])
      chatApiMocks.getSession.mockImplementation(async (id: string) => id === 's1' ? first : second)
      chatApiMocks.sendMessage.mockReturnValue(deferred.promise)

      const { useAutomationChat } = await loadComposable()
      const chat = useAutomationChat()
      await vi.waitFor(() => expect(chat.selectedSession.value?.id).toBe('s1'))

      chat.messageContent.value = 'create card for release notes'
      const pendingSend = chat.handleSendMessage()
      await chat.loadSession('s2')
      deferred.resolve()
      await pendingSend

      expect(chat.selectedSession.value?.id).toBe('s2')
      expect(chatApiMocks.getSession).toHaveBeenCalledTimes(2)
    })

    it('restores the visible session as the refresh target after another session fails to load', async () => {
      const first = { id: 's1', title: 'First', boardId: 'b1', recentMessages: [] }
      const second = { id: 's2', title: 'Second', boardId: 'b1', recentMessages: [] }
      const assistant = {
        id: 'a1', sessionId: 's1', role: 1, messageType: 'proposal-reference',
        proposalId: 'p1', tokenUsage: null, content: 'Proposal created.', createdAt: '2026-05-16T10:01:00Z',
      }
      const refreshedFirst = { ...first, recentMessages: [assistant] }
      chatApiMocks.getMySessions.mockResolvedValue([first, second])
      chatApiMocks.getSession.mockImplementation(async (id: string) => {
        if (id === 's2') throw new Error('session load failed')
        return refreshedFirst
      })
      chatApiMocks.sendMessage.mockResolvedValue(assistant)

      const { useAutomationChat } = await loadComposable()
      const chat = useAutomationChat()
      await vi.waitFor(() => expect(chat.selectedSession.value?.id).toBe('s1'))
      await chat.loadSession('s2')
      chat.messageContent.value = 'create a release card'
      await chat.handleSendMessage()

      expect(chat.messageContent.value).toBe('')
      expect(chat.selectedSession.value?.recentMessages).toEqual([assistant])
      expect(chatApiMocks.getSession).toHaveBeenCalledWith('s1')
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

  describe('board loading recovery', () => {
    it('keeps a board-load error available instead of presenting unavailable boards as empty', async () => {
      boardsApiMocks.getBoards.mockRejectedValue(new Error('Boards unavailable'))

      const { useAutomationChat } = await loadComposable()
      const chat = useAutomationChat()

      await vi.waitFor(() => expect(chat.boardOptionsLoadError.value).toBe('Boards unavailable'))
      expect(chat.eligibleBoards.value).toEqual([])
      expect(chat.boardOptionsLoadError.value).toBe('Boards unavailable')
    })

    it('clears the board-load error only after an explicit retry succeeds', async () => {
      boardsApiMocks.getBoards
        .mockRejectedValueOnce(new Error('Boards unavailable'))
        .mockResolvedValueOnce([
          { id: 'b1', name: 'Release Board', description: null, isArchived: false, canWrite: true },
        ])

      const { useAutomationChat } = await loadComposable()
      const chat = useAutomationChat()
      await vi.waitFor(() => expect(chat.boardOptionsLoadError.value).toBe('Boards unavailable'))

      await expect(chat.loadBoardOptions()).resolves.toBe(true)

      expect(chat.boardOptionsLoadError.value).toBeNull()
      expect(chat.eligibleBoards.value.map((board) => board.id)).toEqual(['b1'])
    })

    it('shows loading during retry and keeps the failure when retry also fails', async () => {
      let rejectRetry!: (reason?: unknown) => void
      const retryPromise = new Promise<unknown[]>((_, reject) => { rejectRetry = reject })
      boardsApiMocks.getBoards
        .mockRejectedValueOnce(new Error('Boards unavailable'))
        .mockReturnValueOnce(retryPromise)

      const { useAutomationChat } = await loadComposable()
      const chat = useAutomationChat()
      await vi.waitFor(() => expect(chat.boardOptionsLoadError.value).toBe('Boards unavailable'))

      const pendingRetry = chat.loadBoardOptions()
      expect(chat.loadingBoards.value).toBe(true)
      expect(chat.boardOptionsLoadError.value).toBeNull()

      rejectRetry(new Error('Still unavailable'))
      await expect(pendingRetry).resolves.toBe(false)
      expect(chat.loadingBoards.value).toBe(false)
      expect(chat.boardOptionsLoadError.value).toBe('Still unavailable')
    })

    it('does not write a late board-load error after disposal', async () => {
      let rejectBoards!: (reason?: unknown) => void
      boardsApiMocks.getBoards.mockReturnValue(new Promise<unknown[]>((_, reject) => { rejectBoards = reject }))

      const { useAutomationChat } = await loadComposable()
      const chat = useAutomationChat()
      for (const fn of scopeDisposeFns) fn()

      rejectBoards(new Error('late board failure'))
      await vi.waitFor(() => expect(boardsApiMocks.getBoards).toHaveBeenCalled())

      expect(chat.boardOptionsLoadError.value).toBeNull()
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

    it('does not create a session after disposal lands while loading board options', async () => {
      // Defer getBoards so disposal can land in the microtask gap between the
      // boards resolving (loadBoardOptions returns true) and handleCreateSession
      // resuming after its `await loadBoardOptions()`.
      let resolveBoards!: (value: unknown[]) => void
      const boardsPromise = new Promise<unknown[]>((resolve) => { resolveBoards = resolve })
      boardsApiMocks.getBoards.mockReturnValue(boardsPromise)

      const { useAutomationChat } = await loadComposable()
      const chat = useAutomationChat()

      chat.newSessionTitle.value = 'Test Session'
      chat.newSessionBoardId.value = 'Project Alpha'

      // Kick off creation without awaiting; it suspends on `await loadBoardOptions()`.
      const pending = chat.handleCreateSession()

      // Dispose AFTER boards resolve but BEFORE handleCreateSession resumes.
      // loadBoardOptions's own internal continuation is registered first, so it
      // resolves to true, then this fires, then handleCreateSession resumes --
      // exactly the post-load, post-dispose race the guard covers.
      void boardsPromise.then(() => {
        for (const fn of scopeDisposeFns) fn()
      })

      resolveBoards([{ id: 'b1', name: 'Project Alpha', description: null, isArchived: false }])

      // Drain microtasks so the deferred dispose and the resumed continuation run.
      await new Promise((resolve) => setTimeout(resolve))
      await pending

      // Post-dispose the function must bail before creating the session or
      // emitting any toast, and must not leave creatingSession stuck true.
      expect(chatApiMocks.createSession).not.toHaveBeenCalled()
      expect(toastMocks.error).not.toHaveBeenCalled()
      expect(chat.creatingSession.value).toBe(false)
    })
  })
})
