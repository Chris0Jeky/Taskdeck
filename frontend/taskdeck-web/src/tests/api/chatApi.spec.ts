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
    vi.mocked(http.get).mockResolvedValue({ data: { providerName: 'Mock' } })

    await chatApi.getHealth()

    expect(http.get).toHaveBeenCalledWith('/llm/chat/health')
  })
})
