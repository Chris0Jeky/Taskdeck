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
      boardLoadError: null,
      ...overrides,
    },
  })
}

describe('ChatMessageList board recovery', () => {
  it('renders the saved source receipt separately from original user intent', () => {
    const wrapper = mountList({ messages: [{ ...messages[0], contextSources: [{ kind: 'private-memory', id: 'm1', title: 'Uncertainty', revision: 3, truncated: true }] }] })
    expect(wrapper.get('.td-message-content').text()).toBe(messages[0]!.content)
    expect(wrapper.get('details').text()).toContain('Private memory')
    expect(wrapper.get('details').text()).toContain('version 3')
    expect(wrapper.get('details').text()).toContain('excerpt')
  })
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

  it('keeps board-load failure separate from the no-board state and offers retry', async () => {
    const wrapper = mountList({ boardLoadError: 'Boards unavailable' })

    expect(wrapper.text()).toContain('Boards unavailable')
    expect(wrapper.text()).toContain('Retry loading boards')
    expect(wrapper.text()).not.toContain('no active boards you can edit')

    await wrapper.get('button.td-btn--secondary').trigger('click')
    expect(wrapper.emitted('reload-boards')).toHaveLength(1)
  })

  it('keeps cached writable choices usable alongside a board-load retry error', async () => {
    const wrapper = mountList({ boardLoadError: 'Boards unavailable' })

    expect(wrapper.text()).toContain('Boards unavailable')
    expect(wrapper.text()).toContain('Retry loading boards')
    expect(wrapper.find('select').exists()).toBe(true)
    expect(wrapper.text()).toContain('Release Board')

    await wrapper.get('select').setValue('board-1')
    await wrapper.get('button.td-btn--primary').trigger('click')

    expect(wrapper.emitted('bind-board')).toEqual([['assistant-1', 'board-1']])
    expect(wrapper.emitted('reload-boards')).toBeUndefined()
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
  it('disables retained continuation while shared thinking or receipt refresh blocks sending', async () => {
    const wrapper = mountList({ selectedSessionBoardId: 'board-1', sendBlocked: true })
    expect(wrapper.get('button.td-btn--primary').attributes('disabled')).toBeDefined()
    await wrapper.get('button.td-btn--primary').trigger('click')
    expect(wrapper.emitted('continue-instruction')).toBeUndefined()
    await wrapper.setProps({ sendBlocked: false })
    await wrapper.get('button.td-btn--primary').trigger('click')
    expect(wrapper.emitted('continue-instruction')).toEqual([['assistant-1']])
  })
})
