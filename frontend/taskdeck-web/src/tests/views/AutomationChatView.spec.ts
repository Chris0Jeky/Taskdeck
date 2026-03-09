import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import AutomationChatView from '../../views/AutomationChatView.vue'

function createDeferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void
  const promise = new Promise<T>((innerResolve) => {
    resolve = innerResolve
  })

  return { promise, resolve }
}

const routerMocks = vi.hoisted(() => ({
  push: vi.fn(),
}))

const routeMock = vi.hoisted(() => ({
  query: {} as Record<string, unknown>,
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
  useRoute: () => routeMock,
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
    routeMock.query = {}
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
      query: { boardId: 'board-1' },
      hash: '#proposal-proposal-1',
    })
  })

  it('prefills the create-session board context from the route boardId query', async () => {
    routeMock.query = { boardId: 'board-1' }

    const wrapper = mountView()
    await waitForAsyncUi()

    expect(wrapper.get('input[placeholder="Board context (optional)"]').element.value).toBe('Board One')
    expect(wrapper.text()).toContain('Board context will stay anchored to Board One.')
  })

  it('keeps the deep-linked board id when duplicate board names exist', async () => {
    routeMock.query = { boardId: 'board-2' }
    mocks.getBoards.mockResolvedValue([
      {
        id: 'board-1',
        name: 'Shared Board',
        description: 'First board',
        isArchived: false,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      },
      {
        id: 'board-2',
        name: 'Shared Board',
        description: 'Second board',
        isArchived: false,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      },
    ])

    const wrapper = mountView()
    await waitForAsyncUi()

    await wrapper.get('input[placeholder="Session title"]').setValue('Scoped session')
    await findButtonByText(wrapper, 'Create Session').trigger('click')
    await waitForAsyncUi()

    expect(mocks.createSession).toHaveBeenCalledWith({
      title: 'Scoped session',
      boardId: 'board-2',
    })
    expect(wrapper.text()).toContain('Board context will stay anchored to Shared Board.')
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

  it('accepts board selection by board id regardless of GUID casing', async () => {
    mocks.getBoards.mockResolvedValue([
      {
        id: 'A1B2C3D4-E5F6-7890-ABCD-EF1234567890',
        name: 'Board One',
        description: 'Primary workspace board',
        isArchived: false,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      },
    ])

    const wrapper = mountView()
    await waitForAsyncUi()

    await wrapper.get('input[placeholder="Session title"]').setValue('Scoped session')
    await wrapper.get('input[placeholder="Board context (optional)"]').setValue('a1b2c3d4-e5f6-7890-abcd-ef1234567890')
    await findButtonByText(wrapper, 'Create Session').trigger('click')
    await waitForAsyncUi()

    expect(mocks.createSession).toHaveBeenCalledWith({
      title: 'Scoped session',
      boardId: 'A1B2C3D4-E5F6-7890-ABCD-EF1234567890',
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

  it('waits for boards to finish loading before validating the board context', async () => {
    const deferredBoards = createDeferred([
      {
        id: 'board-1',
        name: 'Board One',
        description: 'Primary workspace board',
        isArchived: false,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      },
    ])
    mocks.getBoards.mockReturnValueOnce(deferredBoards.promise)

    const wrapper = mountView()
    await Promise.resolve()

    await wrapper.get('input[placeholder="Session title"]').setValue('Scoped session')
    await wrapper.get('input[placeholder="Board context (optional)"]').setValue('Board One')

    const createSessionTrigger = findButtonByText(wrapper, 'Create Session').trigger('click')
    expect(mocks.createSession).not.toHaveBeenCalled()

    deferredBoards.resolve([
      {
        id: 'board-1',
        name: 'Board One',
        description: 'Primary workspace board',
        isArchived: false,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      },
    ])

    await createSessionTrigger
    await waitForAsyncUi()

    expect(mocks.errorToast).not.toHaveBeenCalled()
    expect(mocks.createSession).toHaveBeenCalledWith({
      title: 'Scoped session',
      boardId: 'board-1',
    })
  })

  it('stops session creation when reloading board options fails', async () => {
    mocks.getBoards.mockResolvedValueOnce([
      {
        id: 'board-1',
        name: 'Board One',
        description: 'Primary workspace board',
        isArchived: false,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      },
    ])
    mocks.getBoards.mockRejectedValueOnce(new Error('network down'))

    const wrapper = mountView()
    await waitForAsyncUi()
    mocks.errorToast.mockClear()

    await wrapper.get('input[placeholder="Session title"]').setValue('Scoped session')
    await wrapper.get('input[placeholder="Board context (optional)"]').setValue('Board One')
    await findButtonByText(wrapper, 'Create Session').trigger('click')
    await waitForAsyncUi()

    expect(mocks.createSession).not.toHaveBeenCalled()
    expect(mocks.errorToast).toHaveBeenCalledTimes(1)
    expect(mocks.errorToast).toHaveBeenCalledWith('Failed to load boards')
  })
})
