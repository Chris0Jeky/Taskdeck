import { beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, ref } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import { useAutomationChat } from '../../composables/useAutomationChat'
import type { ChatMessage, ChatSession } from '../../types/chat'

const api = vi.hoisted(() => ({ getMySessions: vi.fn(), getSession: vi.fn(), getHealth: vi.fn(), sendMessage: vi.fn() }))
vi.mock('vue-router', () => ({ useRoute: () => ({ query: {} }), useRouter: () => ({ push: vi.fn() }) }))
vi.mock('../../store/toastStore', () => ({ useToastStore: () => ({ error: vi.fn() }) }))
vi.mock('../../api/boardsApi', () => ({ boardsApi: { getBoards: vi.fn().mockResolvedValue([]) } }))
vi.mock('../../api/chatApi', () => ({ chatApi: api }))
const session = (id = 's1'): ChatSession => ({ id, userId: 'u1', boardId: 'b1', title: 'Thinking', status: 'Active', createdAt: '', updatedAt: '', recentMessages: [] })
const reply: ChatMessage = { id: 'reply', sessionId: 's1', role: 'Assistant', content: 'Saved reply', messageType: 'text', proposalId: null, tokenUsage: null, createdAt: new Date().toISOString() }
async function setup() {
  let chat!: ReturnType<typeof useAutomationChat>
  const blocked = ref(false)
  const wrapper = mount(defineComponent({ setup() { chat = useAutomationChat({ sendBlocked: () => blocked.value }); return () => null } }))
  await flushPromises()
  await chat.loadSession('s1')
  return { chat, wrapper, blocked }
}
describe('companion send continuity', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    api.getSession.mockReset()
    api.getMySessions.mockResolvedValue([])
    api.getHealth.mockResolvedValue({ status: 'healthy' })
    api.getSession.mockImplementation(async (id: string) => session(id))
    api.sendMessage.mockResolvedValue(reply)
  })
  it('blocks normal and clarification sends while thinking is unsaved, then permits a saved draft', async () => {
    const { chat, blocked, wrapper } = await setup()
    blocked.value = true; chat.messageContent.value = 'Use my thinking'
    await chat.handleSendMessage(); await chat.handleSkipClarification()
    expect(api.sendMessage).not.toHaveBeenCalled()
    expect(chat.messageContent.value).toBe('Use my thinking')
    blocked.value = false; await chat.handleSendMessage()
    expect(api.sendMessage).toHaveBeenCalledTimes(1)
    wrapper.unmount()
  })
  it('retains a successful response and retries only the receipt GET after a refresh failure', async () => {
    const { chat, wrapper } = await setup()
    api.getSession.mockRejectedValueOnce(new Error('offline'))
    chat.messageContent.value = 'Use this source'; await chat.handleSendMessage()
    expect(chat.sortedMessages.value.some(message => message.id === 'reply')).toBe(true)
    expect(chat.receiptRefreshError.value).toContain('do not resend')
    const receipt = { ...reply, content: 'Persisted reply with receipt' }
    api.getSession.mockResolvedValueOnce({ ...session(), recentMessages: [receipt] })
    await chat.retryReceiptRefresh()
    expect(api.sendMessage).toHaveBeenCalledTimes(1)
    expect(api.getSession).toHaveBeenLastCalledWith('s1', { skipRetry: true })
    expect(chat.receiptRefreshError.value).toBeNull()
    expect(chat.sortedMessages.value).toEqual([receipt])
    wrapper.unmount()
  })
  it('ignores a pending refresh after changing sessions, including switching back', async () => {
    const { chat, wrapper } = await setup()
    let resolve!: (value: ChatSession) => void
    api.getSession.mockReturnValueOnce(new Promise<ChatSession>(done => { resolve = done }))
    const refresh = chat.retryReceiptRefresh()
    chat.messageContent.value = 'Blocked during refresh'; await chat.handleSendMessage()
    expect(api.sendMessage).not.toHaveBeenCalled()
    await chat.loadSession('s2'); await chat.loadSession('s1')
    resolve({ ...session(), title: 'Stale private receipt' }); await refresh
    expect(chat.selectedSession.value?.title).toBe('Thinking')
    expect(chat.refreshingReceipt.value).toBe(false)
    wrapper.unmount()
  })
})
