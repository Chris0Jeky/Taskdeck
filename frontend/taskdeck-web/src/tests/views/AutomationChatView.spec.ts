import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import AutomationChatView from '../../views/AutomationChatView.vue'

const mocks = vi.hoisted(() => ({
  getMySessions: vi.fn(),
  getSession: vi.fn(),
  sendMessage: vi.fn(),
  createSession: vi.fn(),
  getBoards: vi.fn(),
  approveProposal: vi.fn(),
  executeProposal: vi.fn(),
  createRequestId: vi.fn(),
  successToast: vi.fn(),
  errorToast: vi.fn(),
}))

vi.mock('../../api/chatApi', () => ({
  chatApi: {
    getMySessions: mocks.getMySessions,
    getSession: mocks.getSession,
    sendMessage: mocks.sendMessage,
    createSession: mocks.createSession,
  },
}))

vi.mock('../../api/boardsApi', () => ({
  boardsApi: {
    getBoards: mocks.getBoards,
  },
}))

vi.mock('../../api/automationApi', () => ({
  automationApi: {
    approveProposal: mocks.approveProposal,
    executeProposal: mocks.executeProposal,
  },
}))

vi.mock('../../utils/requestId', () => ({
  createRequestId: mocks.createRequestId,
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => ({
    success: mocks.successToast,
    error: mocks.errorToast,
  }),
}))

vi.mock('../../composables/useErrorMapper', () => ({
  getErrorDisplay: (_error: unknown, fallback: string) => ({ message: fallback }),
}))

function buildSession() {
  const now = new Date().toISOString()
  return {
    id: 'session-1',
    userId: 'user-1',
    boardId: 'board-1',
    title: 'Bootstrap session',
    status: 'Active',
    createdAt: now,
    updatedAt: now,
    recentMessages: [
      {
        id: 'message-1',
        sessionId: 'session-1',
        role: 'Assistant',
        content: 'Checklist bootstrap proposal created',
        messageType: 'proposal-reference',
        proposalId: 'proposal-1',
        tokenUsage: 42,
        createdAt: now,
      },
    ],
  }
}

async function waitForAsyncUi() {
  await Promise.resolve()
  await Promise.resolve()
}

describe('AutomationChatView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    const session = buildSession()
    mocks.getMySessions.mockResolvedValue([session])
    mocks.getSession.mockResolvedValue(session)
    mocks.getBoards.mockResolvedValue([])
    mocks.approveProposal.mockResolvedValue({ id: 'proposal-1' })
    mocks.executeProposal.mockResolvedValue({ id: 'proposal-1' })
    mocks.createRequestId.mockReturnValue('req-123')
  })

  it('approves and executes proposal references from chat in one click', async () => {
    const wrapper = mount(AutomationChatView, {
      global: {
        stubs: {
          InputAssistField: true,
        },
      },
    })

    await waitForAsyncUi()

    const applyButton = wrapper
      .findAll('button')
      .find((button) => button.text().includes('Approve & Execute'))
    expect(applyButton).toBeTruthy()

    await applyButton!.trigger('click')
    await waitForAsyncUi()

    expect(mocks.approveProposal).toHaveBeenCalledWith('proposal-1')
    expect(mocks.executeProposal).toHaveBeenCalledWith('proposal-1', 'req-123')
    expect(mocks.successToast).toHaveBeenCalledWith('Proposal approved and executed')
  })

  it('shows an error toast when proposal application fails', async () => {
    mocks.approveProposal.mockRejectedValue(new Error('approval failed'))

    const wrapper = mount(AutomationChatView, {
      global: {
        stubs: {
          InputAssistField: true,
        },
      },
    })

    await waitForAsyncUi()

    const applyButton = wrapper
      .findAll('button')
      .find((button) => button.text().includes('Approve & Execute'))
    expect(applyButton).toBeTruthy()

    await applyButton!.trigger('click')
    await waitForAsyncUi()

    expect(mocks.executeProposal).not.toHaveBeenCalled()
    expect(mocks.errorToast).toHaveBeenCalledWith('Failed to apply proposal from chat')
  })
})
