import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import ChatMessageList from '../../../components/chat/ChatMessageList.vue'
import type { Board } from '../../../types/board'
import type { ChatMessage } from '../../../types/chat'

const createdAt = '2026-09-07T12:00:00Z'

const messages: ChatMessage[] = [
  {
    id: 'user-1',
    sessionId: 'session-1',
    role: 'User',
    content: 'create card for release notes',
    messageType: 'text',
    proposalId: null,
    tokenUsage: null,
    createdAt,
  },
  {
    id: 'assistant-1',
    sessionId: 'session-1',
    role: 'Assistant',
    content: 'No board is linked, so no proposal was created.',
    messageType: 'action-needs-board',
    proposalId: null,
    tokenUsage: 12,
    createdAt,
  },
]

const boards: Board[] = [
  {
    id: 'board-1',
    name: 'Release Board',
    description: null,
    isArchived: false,
    canWrite: true,
    createdAt,
    updatedAt: createdAt,
  },
  {
    id: 'board-2',
    name: 'Operations Board',
    description: null,
    isArchived: false,
    canWrite: true,
    createdAt,
    updatedAt: createdAt,
  },
]

function mountList(overrides: Record<string, unknown> = {}) {
  return mount(ChatMessageList, {
    props: {
      messages,
      sendingMessage: false,
      eligibleBoards: boards,
      loadingBoards: false,
      selectedSessionBoardId: null,
      selectedSessionBoardName: 'No board context',
      pendingBoardMessageId: 'assistant-1',
      bindingBoard: false,
      bindingMessageId: null,
      boardBindingError: null,
      boardBindingReceipt: null,
      ...overrides,
    },
  })
}

describe('ChatMessageList board recovery', () => {
  it('requires an explicit choice when multiple writable boards are available', async () => {
    const wrapper = mountList()

    const linkButton = wrapper.get('button.td-btn--primary')
    expect(linkButton.attributes('disabled')).toBeDefined()

    await wrapper.get('select').setValue('board-2')
    await linkButton.trigger('click')

    expect(wrapper.emitted('bind-board')).toEqual([['assistant-1', 'board-2']])
  })

  it('offers the sole writable board without binding during render', async () => {
    const wrapper = mountList({ eligibleBoards: [boards[0]] })

    expect(wrapper.emitted('bind-board')).toBeUndefined()
    await wrapper.setProps({ sendingMessage: false })
    expect(wrapper.emitted('bind-board')).toBeUndefined()

    await wrapper.get('button.td-btn--primary').trigger('click')
    expect(wrapper.emitted('bind-board')).toEqual([['assistant-1', 'board-1']])
  })

  it('explains the zero-board state and links to Boards', async () => {
    const wrapper = mountList({ eligibleBoards: [] })

    expect(wrapper.text()).toContain('no active boards you can edit')
    await wrapper.get('button').trigger('click')
    expect(wrapper.emitted('open-boards')).toHaveLength(1)
  })

  it('shows a binding receipt and waits for explicit continuation', async () => {
    const wrapper = mountList({
      selectedSessionBoardId: 'board-1',
      selectedSessionBoardName: 'Release Board',
      boardBindingReceipt: 'Release Board',
    })

    expect(wrapper.text()).toContain('Linked to Release Board')
    expect(wrapper.text()).toContain('has not been sent again')
    expect(wrapper.emitted('continue-instruction')).toBeUndefined()

    await wrapper.setProps({ boardBindingReceipt: 'Release Board' })
    expect(wrapper.emitted('continue-instruction')).toBeUndefined()

    await wrapper.get('button.td-btn--primary').trigger('click')
    expect(wrapper.emitted('continue-instruction')).toEqual([['assistant-1']])
  })
})
