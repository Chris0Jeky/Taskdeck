import { describe, expect, it, vi } from 'vitest'
import { defineComponent } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import { useAutomationChat } from '../../composables/useAutomationChat'
import type { ChatSession } from '../../types/chat'

vi.mock('vue-router', () => ({ useRoute: () => ({ query: {} }), useRouter: () => ({ push: vi.fn() }) }))
vi.mock('../../store/toastStore', () => ({ useToastStore: () => ({ error: vi.fn() }) }))
vi.mock('../../api/boardsApi', () => ({ boardsApi: { getBoards: vi.fn().mockResolvedValue([]) } }))
vi.mock('../../api/chatApi', () => ({ chatApi: {
  getMySessions: vi.fn().mockResolvedValue([]), getHealth: vi.fn().mockResolvedValue({ status: 'healthy' }),
} }))

describe('chat source selection with real Vue watchers', () => {
  it('retains checked sources across same-session refreshes and clears them on identity changes', async () => {
    let chat!: ReturnType<typeof useAutomationChat>
    const wrapper = mount(defineComponent({ setup() { chat = useAutomationChat(); return () => null } }))
    await flushPromises()
    const session: ChatSession = { id: 's1', userId: 'u1', boardId: 'b1', title: 'Context', status: 'Active', createdAt: '', updatedAt: '', recentMessages: [] }
    chat.selectedSession.value = session
    const selection = { cardId: 'c1', includeThinking: true, memories: [{ id: 'm1', revision: 2 }] }
    chat.contextSelection.value = selection
    chat.selectedSession.value = { ...session, updatedAt: 'later', recentMessages: [] }
    expect(chat.contextSelection.value).toEqual(selection)
    chat.selectedSession.value = { ...session, boardId: 'b2' }
    expect(chat.contextSelection.value).toBeNull()
    chat.contextSelection.value = selection
    chat.selectedSession.value = { ...session, id: 's2', boardId: 'b2' }
    expect(chat.contextSelection.value).toBeNull()
    wrapper.unmount()
  })
})
