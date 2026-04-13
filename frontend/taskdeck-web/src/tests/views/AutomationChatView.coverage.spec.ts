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
  useRouter: () => ({ push: routerMocks.push }),
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

function buildSession(overrides: Record<string, unknown> = {}) {
  const now = new Date().toISOString()
  return {
    id: 'session-1',
    userId: 'user-1',
    boardId: 'board-1',
    title: 'Test session',
    status: 'Active',
    createdAt: now,
    updatedAt: now,
    recentMessages: [
      {
        id: 'message-1',
        sessionId: 'session-1',
        role: 'User',
        content: 'Hello there',
        messageType: 'text',
        proposalId: null,
        tokenUsage: null,
        createdAt: now,
      },
      {
        id: 'message-2',
        sessionId: 'session-1',
        role: 'Assistant',
        content: 'Hello! How can I help?',
        messageType: 'text',
        proposalId: null,
        tokenUsage: 50,
        createdAt: now,
      },
    ],
    ...overrides,
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
              if (!firstOption) return
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
  return wrapper.findAll('button').find((node) => node.text().trim() === text)
}

describe('AutomationChatView — message sending flow', () => {
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
      isProbed: false,
      verificationStatus: 'unverified',
    })
    mocks.getBoards.mockResolvedValue([
      {
        id: 'board-1',
        name: 'Board One',
        description: 'Primary board',
        isArchived: false,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      },
    ])
    mocks.createSession.mockResolvedValue({ id: 'session-created' })
    mocks.sendMessage.mockResolvedValue(undefined)
  })

  it('sends a message when Send Message button is clicked', async () => {
    const wrapper = mountView()
    await waitForAsyncUi()

    const textarea = wrapper.find('textarea')
    await textarea.setValue('Create a new card for deployment')

    const sendBtn = findButtonByText(wrapper, 'Send Message')
    expect(sendBtn).toBeDefined()
    await sendBtn!.trigger('click')
    await waitForAsyncUi()

    expect(mocks.sendMessage).toHaveBeenCalledWith(
      'session-1',
      expect.objectContaining({ content: 'Create a new card for deployment' }),
    )
  })

  it('does not send empty messages', async () => {
    const wrapper = mountView()
    await waitForAsyncUi()

    const sendBtn = findButtonByText(wrapper, 'Send Message')
    expect(sendBtn).toBeDefined()
    await sendBtn!.trigger('click')
    await waitForAsyncUi()

    expect(mocks.sendMessage).not.toHaveBeenCalled()
  })

  it('shows error toast when send message fails', async () => {
    mocks.sendMessage.mockRejectedValueOnce(new Error('Send failed'))

    const wrapper = mountView()
    await waitForAsyncUi()

    const textarea = wrapper.find('textarea')
    await textarea.setValue('some message')

    const sendBtn = findButtonByText(wrapper, 'Send Message')
    await sendBtn!.trigger('click')
    await waitForAsyncUi()

    expect(mocks.errorToast).toHaveBeenCalled()
  })

  it('displays user and assistant messages in the chat', async () => {
    const wrapper = mountView()
    await waitForAsyncUi()

    expect(wrapper.text()).toContain('Hello there')
    expect(wrapper.text()).toContain('Hello! How can I help?')
  })

  it('renders message role labels for both user and assistant messages', async () => {
    const wrapper = mountView()
    await waitForAsyncUi()

    const roleLabels = wrapper.findAll('.td-message-role')
    const roleTexts = roleLabels.map((r) => r.text())

    expect(roleTexts).toContain('User')
    expect(roleTexts).toContain('Assistant')
  })
})

describe('AutomationChatView — session creation', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    routeMock.query = {}
    mocks.getMySessions.mockResolvedValue([])
    mocks.getSession.mockResolvedValue(null)
    mocks.getHealth.mockResolvedValue({
      isAvailable: true,
      providerName: 'Mock',
      errorMessage: null,
      model: 'mock-default',
      isMock: true,
      isProbed: false,
      verificationStatus: 'unverified',
    })
    mocks.getBoards.mockResolvedValue([
      {
        id: 'board-1',
        name: 'Board One',
        description: 'Primary board',
        isArchived: false,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      },
    ])
    mocks.createSession.mockResolvedValue({ id: 'session-new' })
    mocks.sendMessage.mockResolvedValue(undefined)
  })

  it('creates a new session with title and board context', async () => {
    const wrapper = mountView()
    await waitForAsyncUi()

    await wrapper.get('input[placeholder="Session title"]').setValue('My new session')
    await wrapper.get('input[placeholder="Board context (optional)"]').setValue('Board One')

    const createBtn = findButtonByText(wrapper, 'Create Session')
    expect(createBtn).toBeDefined()
    await createBtn!.trigger('click')
    await waitForAsyncUi()

    expect(mocks.createSession).toHaveBeenCalledWith({
      title: 'My new session',
      boardId: 'board-1',
    })
  })

  it('creates a session without board context when left blank', async () => {
    const wrapper = mountView()
    await waitForAsyncUi()

    await wrapper.get('input[placeholder="Session title"]').setValue('No board session')

    const createBtn = findButtonByText(wrapper, 'Create Session')
    await createBtn!.trigger('click')
    await waitForAsyncUi()

    expect(mocks.createSession).toHaveBeenCalledWith({
      title: 'No board session',
      boardId: null,
    })
  })

  it('shows error toast when session creation fails', async () => {
    mocks.createSession.mockRejectedValueOnce(new Error('Creation failed'))

    const wrapper = mountView()
    await waitForAsyncUi()

    await wrapper.get('input[placeholder="Session title"]').setValue('Failing session')
    const createBtn = findButtonByText(wrapper, 'Create Session')
    await createBtn!.trigger('click')
    await waitForAsyncUi()

    expect(mocks.errorToast).toHaveBeenCalled()
  })
})

describe('AutomationChatView — session list', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    routeMock.query = {}
    const now = new Date().toISOString()
    mocks.getMySessions.mockResolvedValue([
      buildSession({ id: 'session-1', title: 'First session' }),
      buildSession({ id: 'session-2', title: 'Second session' }),
    ])
    mocks.getSession.mockResolvedValue(buildSession({ id: 'session-1', title: 'First session' }))
    mocks.getHealth.mockResolvedValue({
      isAvailable: true,
      providerName: 'Mock',
      errorMessage: null,
      model: 'mock-default',
      isMock: true,
      isProbed: false,
      verificationStatus: 'unverified',
    })
    mocks.getBoards.mockResolvedValue([
      {
        id: 'board-1',
        name: 'Board One',
        description: 'Primary board',
        isArchived: false,
        createdAt: now,
        updatedAt: now,
      },
    ])
    mocks.sendMessage.mockResolvedValue(undefined)
  })

  it('shows the session list with multiple sessions', async () => {
    const wrapper = mountView()
    await waitForAsyncUi()

    expect(wrapper.text()).toContain('First session')
    expect(wrapper.text()).toContain('Second session')
  })

  it('switches session when a different session button is clicked', async () => {
    mocks.getSession.mockImplementation(async (id: string) => {
      return buildSession({ id, title: id === 'session-1' ? 'First session' : 'Second session' })
    })

    const wrapper = mountView()
    await waitForAsyncUi()

    const sessionBtn = wrapper.findAll('button').find((b) => b.text().includes('Second session'))
    expect(sessionBtn).toBeDefined()
    await sessionBtn!.trigger('click')
    await waitForAsyncUi()

    expect(mocks.getSession).toHaveBeenCalledWith('session-2')
  })

  it('shows sessions loading indicator when sessions are being fetched', async () => {
    const deferred = createDeferred<ReturnType<typeof buildSession>[]>()
    mocks.getMySessions.mockReturnValue(deferred.promise)

    const wrapper = mountView()
    await Promise.resolve()

    // While loading, there should be no session list items, just the loading state
    expect(wrapper.text()).toContain('Loading')
  })
})

describe('AutomationChatView — LLM health states', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    routeMock.query = {}
    mocks.getMySessions.mockResolvedValue([buildSession()])
    mocks.getSession.mockResolvedValue(buildSession())
    mocks.getBoards.mockResolvedValue([])
    mocks.sendMessage.mockResolvedValue(undefined)
  })

  it('shows unavailable state when provider is not available', async () => {
    mocks.getHealth.mockResolvedValue({
      isAvailable: false,
      providerName: null,
      errorMessage: 'No provider configured',
      model: null,
      isMock: false,
      isProbed: false,
      verificationStatus: 'unverified',
    })

    const wrapper = mountView()
    await waitForAsyncUi()

    expect(wrapper.text()).toContain('Live LLM unavailable')
  })

  it('shows probing banner when verify button is clicked', async () => {
    mocks.getHealth
      .mockResolvedValueOnce({
        isAvailable: true,
        providerName: 'OpenAI',
        errorMessage: null,
        model: 'gpt-4o-mini',
        isMock: false,
        isProbed: false,
        verificationStatus: 'unverified',
      })
      .mockResolvedValueOnce({
        isAvailable: true,
        providerName: 'OpenAI',
        errorMessage: null,
        model: 'gpt-4o-mini',
        isMock: false,
        isProbed: true,
        verificationStatus: 'verified',
      })

    const wrapper = mountView()
    await waitForAsyncUi()

    expect(wrapper.text()).toContain('Live LLM configured')

    const verifyBtn = findButtonByText(wrapper, 'Verify LLM')
    await verifyBtn!.trigger('click')
    await waitForAsyncUi()

    expect(mocks.getHealth).toHaveBeenCalledWith({ probe: true })
    expect(wrapper.text()).toContain('Live LLM verified')
  })
})
