import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '../../api/http'
import { chatApi } from '../../api/chatApi'

vi.mock('../../api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
  },
}))

describe('chatApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('creates chat session', async () => {
    vi.mocked(http.post).mockResolvedValue({ data: { id: 'session-1' } })

    await chatApi.createSession({ title: 'Session 1', boardId: null })

    expect(http.post).toHaveBeenCalledWith('/llm/chat/sessions', { title: 'Session 1', boardId: null })
  })

  it('sends chat message with proposal flag', async () => {
    vi.mocked(http.post).mockResolvedValue({ data: { id: 'message-1' } })

    await chatApi.sendMessage('session/1', { content: 'create card "x"', requestProposal: true })

    expect(http.post).toHaveBeenCalledWith('/llm/chat/sessions/session%2F1/messages', {
      content: 'create card "x"',
      requestProposal: true,
    })
  })

  it('loads provider health', async () => {
    const healthPayload = {
      isAvailable: true,
      providerName: 'Mock',
      errorMessage: null,
      model: 'mock-default',
      isMock: true,
    }
    vi.mocked(http.get).mockResolvedValue({ data: healthPayload })

    await expect(chatApi.getHealth()).resolves.toEqual(healthPayload)

    expect(http.get).toHaveBeenCalledWith('/llm/chat/health')
  })
})
