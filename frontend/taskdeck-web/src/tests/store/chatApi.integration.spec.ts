/**
 * chatApi integration tests — full API module boundary with mocked HTTP.
 *
 * No chatStore exists; the chatApi is consumed directly by views and composables.
 * These tests exercise the chatApi → http chain covering the full chat lifecycle:
 * session creation, message accumulation, session listing, health checks, and
 * error propagation for each endpoint.
 */
import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '../../api/http'
import { chatApi } from '../../api/chatApi'
import type { ChatMessage, ChatProviderHealth, ChatSession } from '../../types/chat'

vi.mock('../../api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    patch: vi.fn(),
    delete: vi.fn(),
  },
}))

function makeChatSession(overrides: Partial<ChatSession> = {}): ChatSession {
  return {
    id: 'session-1',
    userId: 'user-1',
    boardId: null,
    title: 'Test Session',
    status: 'Active',
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    recentMessages: [],
    ...overrides,
  }
}

function makeChatMessage(overrides: Partial<ChatMessage> = {}): ChatMessage {
  return {
    id: 'msg-1',
    sessionId: 'session-1',
    role: 'User',
    content: 'Hello',
    messageType: 'text',
    proposalId: null,
    tokenUsage: null,
    createdAt: '2026-01-01T00:00:00Z',
    ...overrides,
  }
}

function makeHealthPayload(overrides: Partial<ChatProviderHealth> = {}): ChatProviderHealth {
  return {
    isAvailable: true,
    providerName: 'Mock',
    errorMessage: null,
    model: 'mock-default',
    isMock: true,
    isProbed: false,
    verificationStatus: 'unverified',
    ...overrides,
  }
}

describe('chatApi — integration (mocked HTTP)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  // ── createSession ──────────────────────────────────────────────────────────

  describe('createSession', () => {
    it('posts to /llm/chat/sessions with title and boardId', async () => {
      const session = makeChatSession()
      vi.mocked(http.post).mockResolvedValue({ data: session })

      const result = await chatApi.createSession({ title: 'Test Session', boardId: null })

      expect(result.id).toBe('session-1')
      expect(result.title).toBe('Test Session')
      expect(http.post).toHaveBeenCalledWith('/llm/chat/sessions', {
        title: 'Test Session',
        boardId: null,
      })
    })

    it('associates session with a board when boardId is provided', async () => {
      const session = makeChatSession({ boardId: 'board-42' })
      vi.mocked(http.post).mockResolvedValue({ data: session })

      const result = await chatApi.createSession({ title: 'Board Chat', boardId: 'board-42' })

      expect(result.boardId).toBe('board-42')
      expect(http.post).toHaveBeenCalledWith('/llm/chat/sessions', {
        title: 'Board Chat',
        boardId: 'board-42',
      })
    })

    it('propagates errors from the create session endpoint', async () => {
      vi.mocked(http.post).mockRejectedValue({
        response: { status: 503, data: { message: 'Provider unavailable' } },
      })

      await expect(chatApi.createSession({ title: 'Fail' })).rejects.toBeDefined()
    })
  })

  // ── getMySessions ──────────────────────────────────────────────────────────

  describe('getMySessions', () => {
    it('calls GET /llm/chat/sessions and returns the session list', async () => {
      const sessions = [
        makeChatSession({ id: 'session-1' }),
        makeChatSession({ id: 'session-2', title: 'Second' }),
      ]
      vi.mocked(http.get).mockResolvedValue({ data: sessions })

      const result = await chatApi.getMySessions()

      expect(result).toHaveLength(2)
      expect(result[0].id).toBe('session-1')
      expect(result[1].id).toBe('session-2')
      expect(http.get).toHaveBeenCalledWith('/llm/chat/sessions')
    })

    it('returns empty array when user has no sessions', async () => {
      vi.mocked(http.get).mockResolvedValue({ data: [] })

      const result = await chatApi.getMySessions()

      expect(result).toHaveLength(0)
    })

    it('propagates network errors from session listing', async () => {
      vi.mocked(http.get).mockRejectedValue(new Error('Network Error'))

      await expect(chatApi.getMySessions()).rejects.toThrow('Network Error')
    })
  })

  // ── getSession ─────────────────────────────────────────────────────────────

  describe('getSession', () => {
    it('calls GET /llm/chat/sessions/:id and returns session with recent messages', async () => {
      const session = makeChatSession({
        recentMessages: [
          makeChatMessage({ id: 'msg-1', role: 'User', content: 'Hello' }),
          makeChatMessage({ id: 'msg-2', role: 'Assistant', content: 'Hi there' }),
        ],
      })
      vi.mocked(http.get).mockResolvedValue({ data: session })

      const result = await chatApi.getSession('session-1')

      expect(result.recentMessages).toHaveLength(2)
      expect(result.recentMessages[0].role).toBe('User')
      expect(result.recentMessages[1].role).toBe('Assistant')
      expect(http.get).toHaveBeenCalledWith('/llm/chat/sessions/session-1')
    })

    it('URL-encodes special characters in the session ID', async () => {
      vi.mocked(http.get).mockResolvedValue({ data: makeChatSession({ id: 'session/special' }) })

      await chatApi.getSession('session/special')

      expect(http.get).toHaveBeenCalledWith('/llm/chat/sessions/session%2Fspecial')
    })

    it('propagates 404 when session does not exist', async () => {
      vi.mocked(http.get).mockRejectedValue({
        response: { status: 404, data: { message: 'Session not found' } },
      })

      await expect(chatApi.getSession('missing')).rejects.toBeDefined()
    })
  })

  // ── sendMessage ────────────────────────────────────────────────────────────

  describe('sendMessage', () => {
    it('posts to /llm/chat/sessions/:id/messages with content', async () => {
      const message = makeChatMessage({ id: 'msg-new', content: 'Create a task', role: 'User' })
      vi.mocked(http.post).mockResolvedValue({ data: message })

      const result = await chatApi.sendMessage('session-1', {
        content: 'Create a task',
        requestProposal: false,
      })

      expect(result.id).toBe('msg-new')
      expect(result.content).toBe('Create a task')
      expect(http.post).toHaveBeenCalledWith('/llm/chat/sessions/session-1/messages', {
        content: 'Create a task',
        requestProposal: false,
      })
    })

    it('includes requestProposal flag for proposal-generating messages', async () => {
      const message = makeChatMessage({
        id: 'msg-proposal',
        content: 'Add card "Fix login"',
        messageType: 'text',
      })
      vi.mocked(http.post).mockResolvedValue({ data: message })

      await chatApi.sendMessage('session-1', {
        content: 'Add card "Fix login"',
        requestProposal: true,
      })

      expect(http.post).toHaveBeenCalledWith(
        '/llm/chat/sessions/session-1/messages',
        expect.objectContaining({ requestProposal: true }),
      )
    })

    it('URL-encodes special characters in the session ID for message posting', async () => {
      vi.mocked(http.post).mockResolvedValue({ data: makeChatMessage() })

      await chatApi.sendMessage('session/id+special', { content: 'test' })

      const calledUrl = vi.mocked(http.post).mock.calls[0][0] as string
      expect(calledUrl).toContain('session%2Fid%2Bspecial')
    })

    it('returns messages with proposal references when the assistant generates a proposal', async () => {
      const assistantMsg = makeChatMessage({
        id: 'msg-assistant',
        role: 'Assistant',
        content: 'I created a proposal to add card "Fix login"',
        messageType: 'proposal-reference',
        proposalId: 'proposal-abc',
      })
      vi.mocked(http.post).mockResolvedValue({ data: assistantMsg })

      const result = await chatApi.sendMessage('session-1', {
        content: 'Add card',
        requestProposal: true,
      })

      expect(result.messageType).toBe('proposal-reference')
      expect(result.proposalId).toBe('proposal-abc')
    })

    it('returns degraded messages when the provider is partially available', async () => {
      const degradedMsg = makeChatMessage({
        id: 'msg-degraded',
        role: 'Assistant',
        content: 'I understood your request but could not fully process it.',
        messageType: 'degraded',
        degradedReason: 'Rate limit exceeded',
      })
      vi.mocked(http.post).mockResolvedValue({ data: degradedMsg })

      const result = await chatApi.sendMessage('session-1', { content: 'test' })

      expect(result.messageType).toBe('degraded')
      expect(result.degradedReason).toBe('Rate limit exceeded')
    })

    it('propagates 503 when the LLM provider is unavailable', async () => {
      vi.mocked(http.post).mockRejectedValue({
        response: { status: 503, data: { message: 'LLM provider unavailable' } },
      })

      await expect(
        chatApi.sendMessage('session-1', { content: 'test' }),
      ).rejects.toBeDefined()
    })
  })

  // ── getHealth ──────────────────────────────────────────────────────────────

  describe('getHealth', () => {
    it('calls GET /llm/chat/health without probe by default', async () => {
      vi.mocked(http.get).mockResolvedValue({ data: makeHealthPayload() })

      const result = await chatApi.getHealth()

      expect(result.isAvailable).toBe(true)
      expect(result.providerName).toBe('Mock')
      expect(http.get).toHaveBeenCalledWith('/llm/chat/health')
    })

    it('appends probe=true when probe option is set', async () => {
      vi.mocked(http.get).mockResolvedValue({ data: makeHealthPayload({ isProbed: true }) })

      const result = await chatApi.getHealth({ probe: true })

      expect(result.isProbed).toBe(true)
      expect(http.get).toHaveBeenCalledWith('/llm/chat/health?probe=true')
    })

    it('does not append probe parameter when probe is false', async () => {
      vi.mocked(http.get).mockResolvedValue({ data: makeHealthPayload() })

      await chatApi.getHealth({ probe: false })

      expect(http.get).toHaveBeenCalledWith('/llm/chat/health')
    })

    it('reports provider unavailability from health response', async () => {
      const unhealthy = makeHealthPayload({
        isAvailable: false,
        errorMessage: 'API key expired',
        providerName: 'OpenAI',
        isMock: false,
      })
      vi.mocked(http.get).mockResolvedValue({ data: unhealthy })

      const result = await chatApi.getHealth()

      expect(result.isAvailable).toBe(false)
      expect(result.errorMessage).toBe('API key expired')
    })

    it('propagates errors from the health endpoint', async () => {
      vi.mocked(http.get).mockRejectedValue(new Error('Timeout'))

      await expect(chatApi.getHealth()).rejects.toThrow('Timeout')
    })
  })

  // ── full lifecycle: session → messages ─────────────────────────────────────

  describe('full lifecycle', () => {
    it('creates a session and accumulates multiple messages', async () => {
      const session = makeChatSession({ id: 'lifecycle-session' })
      vi.mocked(http.post).mockResolvedValueOnce({ data: session })

      const created = await chatApi.createSession({ title: 'Lifecycle Test' })
      expect(created.id).toBe('lifecycle-session')

      // Send first user message
      const userMsg = makeChatMessage({ id: 'msg-1', sessionId: 'lifecycle-session', role: 'User', content: 'Hello' })
      vi.mocked(http.post).mockResolvedValueOnce({ data: userMsg })

      const msg1 = await chatApi.sendMessage('lifecycle-session', { content: 'Hello' })
      expect(msg1.sessionId).toBe('lifecycle-session')

      // Send second message with proposal
      const proposalMsg = makeChatMessage({
        id: 'msg-2',
        sessionId: 'lifecycle-session',
        role: 'Assistant',
        content: 'Created proposal',
        messageType: 'proposal-reference',
        proposalId: 'prop-1',
      })
      vi.mocked(http.post).mockResolvedValueOnce({ data: proposalMsg })

      const msg2 = await chatApi.sendMessage('lifecycle-session', {
        content: 'Create card "Fix bug"',
        requestProposal: true,
      })
      expect(msg2.proposalId).toBe('prop-1')

      // Reload session with accumulated messages
      const reloadedSession = makeChatSession({
        id: 'lifecycle-session',
        recentMessages: [userMsg, proposalMsg],
      })
      vi.mocked(http.get).mockResolvedValueOnce({ data: reloadedSession })

      const reloaded = await chatApi.getSession('lifecycle-session')
      expect(reloaded.recentMessages).toHaveLength(2)
      expect(reloaded.recentMessages[1].proposalId).toBe('prop-1')
    })

    it('handles tool call metadata in assistant messages', async () => {
      const toolCallJson = JSON.stringify({
        rounds: 2,
        total_tokens: 150,
        tool_calls: [
          { round: 1, tool: 'create_card', args: { title: 'Fix bug' }, result_summary: 'Card created', is_error: false },
        ],
      })
      const assistantMsg = makeChatMessage({
        id: 'msg-tool',
        role: 'Assistant',
        content: 'Done',
        toolCallMetadataJson: toolCallJson,
      })
      vi.mocked(http.post).mockResolvedValue({ data: assistantMsg })

      const result = await chatApi.sendMessage('session-1', { content: 'Create card' })

      expect(result.toolCallMetadataJson).toBe(toolCallJson)
      // Verify the JSON can be parsed to the expected shape
      const parsed = JSON.parse(result.toolCallMetadataJson!)
      expect(parsed.rounds).toBe(2)
      expect(parsed.tool_calls[0].tool).toBe('create_card')
    })
  })
})
