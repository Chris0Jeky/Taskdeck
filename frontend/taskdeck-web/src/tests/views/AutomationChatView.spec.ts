import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import AutomationChatView from '../../views/AutomationChatView.vue'

const routerMocks = vi.hoisted(() => ({
  push: vi.fn(),
}))

const mocks = vi.hoisted(() => ({
  getMySessions: vi.fn(),
  getSession: vi.fn(),
  sendMessage: vi.fn(),
  createSession: vi.fn(),
  getBoards: vi.fn(),
  successToast: vi.fn(),
  errorToast: vi.fn(),
}))

vi.mock('vue-router', () => ({
  useRouter: () => ({
    push: routerMocks.push,
  }),
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

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => ({
    success: mocks.successToast,
    error: mocks.errorToast,
  }),
}))

vi.mock('../../composables/useErrorMapper', () => ({
  getErrorDisplay: (_error: unknown, fallback: string) => ({ message: fallback }),
}))

function buildSession(messageType = 'proposal-reference') {
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
        messageType,
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
  await Promise.resolve()
}

function mountView() {
  return mount(AutomationChatView, {
    global: {
      stubs: {
        InputAssistField: {
          props: ['modelValue', 'placeholder'],
          emits: ['update:modelValue'],
          template: `
            <input
              class="td-input-assist-stub"
              :placeholder="placeholder"
              :value="modelValue"
              @input="$emit('update:modelValue', $event.target.value)"
            />
          `,
        },
      },
    },
  })
}

function findButtonByText(wrapper: ReturnType<typeof mount>, text: string) {
  const button = wrapper.findAll('button').find((node) => node.text().trim() === text)
  expect(button).toBeTruthy()
  return button!
}

describe('AutomationChatView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    const session = buildSession()
    mocks.getMySessions.mockResolvedValue([session])
    mocks.getSession.mockResolvedValue(session)
    mocks.getBoards.mockResolvedValue([
      {
        id: 'board-1',
        name: 'Board One',
        description: 'Primary workspace board',
        isArchived: false,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      },
    ])
    mocks.createSession.mockResolvedValue({ id: 'session-created' })
    mocks.sendMessage.mockResolvedValue(undefined)
  })

  it('opens linked proposals in Review instead of approving them inline', async () => {
    const wrapper = mountView()
    await waitForAsyncUi()

    expect(wrapper.text()).not.toContain('Approve & Execute')

    const reviewButton = wrapper
      .findAll('button')
      .find((button) => button.text().includes('Open in Review'))
    expect(reviewButton).toBeTruthy()

    await reviewButton!.trigger('click')

    expect(routerMocks.push).toHaveBeenCalledWith({
      name: 'workspace-review',
      hash: '#proposal-proposal-1',
    })
  })

  it('rejects unknown board context values when creating a session', async () => {
    const wrapper = mountView()
    await waitForAsyncUi()

    await wrapper.get('input[placeholder="Session title"]').setValue('Scoped session')
    await wrapper.get('input[placeholder="Board context (optional)"]').setValue('mystery board')
    await findButtonByText(wrapper, 'Create Session').trigger('click')
    await waitForAsyncUi()

    expect(mocks.createSession).not.toHaveBeenCalled()
    expect(mocks.errorToast).toHaveBeenCalledWith('Choose a board from the list or leave board context blank.')
  })

  it('accepts board selection by name and stores the linked board id', async () => {
    const wrapper = mountView()
    await waitForAsyncUi()

    await wrapper.get('input[placeholder="Session title"]').setValue('Scoped session')
    await wrapper.get('input[placeholder="Board context (optional)"]').setValue('Board One')
    await findButtonByText(wrapper, 'Create Session').trigger('click')
    await waitForAsyncUi()

    expect(mocks.createSession).toHaveBeenCalledWith({
      title: 'Scoped session',
      boardId: 'board-1',
    })
  })

  it('does not show proposal review action for non proposal-reference messages', async () => {
    const session = buildSession('status')
    mocks.getMySessions.mockResolvedValue([session])
    mocks.getSession.mockResolvedValue(session)

    const wrapper = mountView()
    await waitForAsyncUi()

    expect(wrapper.text()).not.toContain('Open in Review')
  })
})
