import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { defineComponent } from 'vue'
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
  getHealth: vi.fn(),
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
    getHealth: mocks.getHealth,
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
  getErrorDisplay: (error: unknown, fallback: string) => {
    if (typeof error === 'object' && error !== null) {
      const typed = error as { message?: unknown }
      if (typeof typed.message === 'string' && typed.message.trim().length > 0) {
        return { message: typed.message, code: null }
      }
    }

    return { message: fallback, code: null }
  },
}))

function buildSession(
  messageType = 'proposal-reference',
  overrides: Partial<ReturnType<typeof buildSessionBase>> = {},
) {
  return {
    ...buildSessionBase(messageType),
    ...overrides,
  }
}

function buildSessionBase(messageType = 'proposal-reference') {
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
  } as const
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
        InputAssistField: defineComponent({
          props: {
            modelValue: { type: String, required: true },
            placeholder: { type: String, default: '' },
            options: { type: Array, default: () => [] },
          },
          emits: ['update:modelValue', 'select'],
          methods: {
            emitInput(event: Event) {
              const target = event.target as HTMLInputElement
              this.$emit('update:modelValue', target.value)
            },
            selectFirstOption() {
              const firstOption = (this.options as Array<{ value: string }>)[0]
              if (!firstOption) {
                return
              }

              this.$emit('update:modelValue', firstOption.value)
              this.$emit('select', firstOption)
            },
          },
          template: `
            <div>
              <input
                class="td-input-assist-stub"
                :placeholder="placeholder"
                :value="modelValue"
                @input="emitInput"
              />
              <button
                type="button"
                class="td-input-assist-select-first"
                @click="selectFirstOption"
              >
                Select first option
              </button>
            </div>
          `,
        }),
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
    mocks.getHealth.mockResolvedValue({
      isAvailable: true,
      providerName: 'Mock',
      errorMessage: null,
      model: 'mock-default',
      isMock: true,
    })
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

  it('keeps the selected session board scope when returning to Review', async () => {
    const wrapper = mountView()
    await waitForAsyncUi()

    await findButtonByText(wrapper, 'Back to Review').trigger('click')

    expect(routerMocks.push).toHaveBeenCalledWith({
      name: 'workspace-review',
      query: { boardId: 'board-1' },
    })
  })

  it('falls back to the deep-linked board scope when the selected session has no board context', async () => {
    routeMock.query = { boardId: 'board-2' }
    const sessionWithoutBoard = buildSession('proposal-reference', { boardId: null })
    mocks.getMySessions.mockResolvedValue([sessionWithoutBoard])
    mocks.getSession.mockResolvedValue(sessionWithoutBoard)

    const wrapper = mountView()
    await waitForAsyncUi()

    await findButtonByText(wrapper, 'Back to Review').trigger('click')

    expect(routerMocks.push).toHaveBeenCalledWith({
      name: 'workspace-review',
      query: { boardId: 'board-2' },
    })
  })

  it('shows an explicit mock-provider warning when live llm is not active', async () => {
    const wrapper = mountView()
    await waitForAsyncUi()

    expect(wrapper.text()).toContain('Live LLM not active')
    expect(wrapper.text()).toContain('Mock provider')
    expect(wrapper.get('[data-llm-health-state="mock"]').exists()).toBe(true)
  })

  it('shows a ready banner when a live provider is available', async () => {
    mocks.getHealth.mockResolvedValue({
      isAvailable: true,
      providerName: 'Gemini',
      errorMessage: null,
      model: 'gemini-2.5-flash',
      isMock: false,
    })

    const wrapper = mountView()
    await waitForAsyncUi()

    expect(wrapper.text()).toContain('Live LLM configured')
    expect(wrapper.text()).toContain('Gemini (gemini-2.5-flash)')
    expect(wrapper.text()).toContain('does not prove the upstream provider accepted a live request yet')
    expect(wrapper.get('[data-llm-health-state="configured"]').exists()).toBe(true)
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

  it('keeps the selected board label visible after choosing from the assist list', async () => {
    const wrapper = mountView()
    await waitForAsyncUi()

    await wrapper.get('.td-input-assist-select-first').trigger('click')
    await waitForAsyncUi()

    expect(wrapper.get('input[placeholder="Board context (optional)"]').element.value).toBe('Board One')
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
    expect(mocks.errorToast).toHaveBeenCalledWith('network down')
  })

  it('surfaces provider-health loading failures explicitly', async () => {
    mocks.getHealth.mockRejectedValueOnce(new Error('health down'))

    const wrapper = mountView()
    await waitForAsyncUi()

    expect(wrapper.text()).toContain('LLM status unavailable')
    expect(wrapper.text()).toContain('health down')
    expect(mocks.errorToast).toHaveBeenCalledWith('health down')
  })
})
