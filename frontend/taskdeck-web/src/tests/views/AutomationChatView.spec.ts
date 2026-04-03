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
      isProbed: false,
      verificationStatus: 'unverified',
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

  it('shows a configured banner when a live provider is available', async () => {
    mocks.getHealth.mockResolvedValue({
      isAvailable: true,
      providerName: 'Gemini',
      errorMessage: null,
      model: 'gemini-2.5-flash',
      isMock: false,
      isProbed: false,
      verificationStatus: 'unverified',
    })

    const wrapper = mountView()
    await waitForAsyncUi()

    expect(wrapper.text()).toContain('Live LLM configured')
    expect(wrapper.text()).toContain('Gemini (gemini-2.5-flash)')
    expect(wrapper.text()).toContain('does not prove the upstream provider accepted a live request yet')
    expect(wrapper.get('[data-llm-health-state="configured"]').exists()).toBe(true)
  })

  it('shows a verified banner when probe confirms live reachability', async () => {
    mocks.getHealth.mockResolvedValue({
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

    expect(wrapper.text()).toContain('Live LLM verified')
    expect(wrapper.text()).toContain('probe confirmed reachability')
    expect(wrapper.get('[data-llm-health-state="verified"]').exists()).toBe(true)
  })

  it('shows a failed banner when probe confirms provider is unreachable', async () => {
    mocks.getHealth.mockResolvedValue({
      isAvailable: false,
      providerName: 'OpenAI',
      errorMessage: 'Connection refused',
      model: 'gpt-4o-mini',
      isMock: false,
      isProbed: true,
      verificationStatus: 'failed',
    })

    const wrapper = mountView()
    await waitForAsyncUi()

    expect(wrapper.text()).toContain('LLM verification failed')
    expect(wrapper.text()).toContain('OpenAI (gpt-4o-mini) verification failed: Connection refused')
    expect(wrapper.get('[data-llm-health-state="failed"]').exists()).toBe(true)
  })

  it('shows failed banner with generic message when no error detail is provided', async () => {
    mocks.getHealth.mockResolvedValue({
      isAvailable: false,
      providerName: 'Gemini',
      errorMessage: null,
      model: 'gemini-2.5-flash',
      isMock: false,
      isProbed: true,
      verificationStatus: 'failed',
    })

    const wrapper = mountView()
    await waitForAsyncUi()

    expect(wrapper.text()).toContain('LLM verification failed')
    expect(wrapper.text()).toContain('verification failed. The probe could not confirm reachability.')
    expect(wrapper.get('[data-llm-health-state="failed"]').exists()).toBe(true)
  })

  it('renders degraded messages with warning styling and reason', async () => {
    const now = new Date().toISOString()
    const degradedSession = {
      id: 'session-degraded',
      userId: 'user-1',
      boardId: null,
      title: 'Degraded test',
      status: 'Active',
      createdAt: now,
      updatedAt: now,
      recentMessages: [
        {
          id: 'msg-user',
          sessionId: 'session-degraded',
          role: 'User',
          content: 'Hello',
          messageType: 'text',
          proposalId: null,
          tokenUsage: null,
          createdAt: now,
        },
        {
          id: 'msg-degraded',
          sessionId: 'session-degraded',
          role: 'Assistant',
          content: 'I can help with that request. (Live provider request failed.)',
          messageType: 'degraded',
          proposalId: null,
          tokenUsage: 10,
          createdAt: now,
          degradedReason: 'Live provider request failed.',
        },
      ],
    }
    mocks.getMySessions.mockResolvedValue([degradedSession])
    mocks.getSession.mockResolvedValue(degradedSession)

    const wrapper = mountView()
    await waitForAsyncUi()

    const sessionBtn = wrapper.findAll('button').find((b) => b.text().includes('Degraded test'))
    expect(sessionBtn).toBeTruthy()
    await sessionBtn!.trigger('click')
    await waitForAsyncUi()

    const degradedMsg = wrapper.find('[data-message-type="degraded"]')
    expect(degradedMsg.exists()).toBe(true)
    expect(degradedMsg.classes()).toContain('td-message--degraded')
    expect(degradedMsg.text()).toContain('Degraded response: Live provider request failed.')
  })

  it('sends probe=true when Verify LLM button is clicked', async () => {
    const wrapper = mountView()
    await waitForAsyncUi()

    mocks.getHealth.mockClear()
    mocks.getHealth.mockResolvedValue({
      isAvailable: true,
      providerName: 'OpenAI',
      errorMessage: null,
      model: 'gpt-4o-mini',
      isMock: false,
      isProbed: true,
      verificationStatus: 'verified',
    })

    const verifyBtn = findButtonByText(wrapper, 'Verify LLM')
    expect(verifyBtn).toBeTruthy()
    await verifyBtn!.trigger('click')
    await waitForAsyncUi()

    expect(mocks.getHealth).toHaveBeenCalledWith({ probe: true })
    expect(wrapper.text()).toContain('Live LLM verified')
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

  it('renders a hint card for parse-hint messages with try-this-instead button', async () => {
    const now = new Date().toISOString()
    const hintPayload = JSON.stringify({
      supportedPatterns: [
        'create card "title"',
        'move card {id} to column "name"',
        'archive card {id}',
      ],
      exampleInstruction: 'create card "My new task"',
      closestPattern: 'create card "title"',
      detectedIntent: 'create',
    })
    const hintSession = {
      id: 'session-hint',
      userId: 'user-1',
      boardId: 'board-1',
      title: 'Hint test',
      status: 'Active',
      createdAt: now,
      updatedAt: now,
      recentMessages: [
        {
          id: 'msg-hint',
          sessionId: 'session-hint',
          role: 'Assistant',
          content: `Some LLM response\n\nCould not parse.\nCould not parse instruction into a proposal.[PARSE_HINT]${hintPayload}`,
          messageType: 'parse-hint' as const,
          proposalId: null,
          tokenUsage: 10,
          createdAt: now,
        },
      ],
    }
    mocks.getMySessions.mockResolvedValue([hintSession])
    mocks.getSession.mockResolvedValue(hintSession)

    const wrapper = mountView()
    await waitForAsyncUi()

    // Should show hint card
    expect(wrapper.find('.td-hint-card').exists()).toBe(true)
    expect(wrapper.text()).toContain('Detected intent: create')
    expect(wrapper.text()).toContain('create card "title"')

    // Should have "Try this instead" button
    const tryBtn = wrapper.findAll('button').find((b) => b.text().includes('Try this instead'))
    expect(tryBtn).toBeTruthy()

    // Click should pre-fill the message input
    await tryBtn!.trigger('click')
    const textarea = wrapper.find('textarea')
    expect(textarea.element.value).toBe('create card "My new task"')
  })

  it('toggles the supported patterns list in a hint card', async () => {
    const now = new Date().toISOString()
    const hintPayload = JSON.stringify({
      supportedPatterns: ['create card "title"', 'archive card {id}'],
      exampleInstruction: 'create card "My task"',
      closestPattern: 'create card "title"',
      detectedIntent: null,
    })
    const hintSession = {
      id: 'session-hint2',
      userId: 'user-1',
      boardId: 'board-1',
      title: 'Toggle test',
      status: 'Active',
      createdAt: now,
      updatedAt: now,
      recentMessages: [
        {
          id: 'msg-hint2',
          sessionId: 'session-hint2',
          role: 'Assistant',
          content: `Response\nCould not parse instruction into a proposal.[PARSE_HINT]${hintPayload}`,
          messageType: 'parse-hint' as const,
          proposalId: null,
          tokenUsage: 10,
          createdAt: now,
        },
      ],
    }
    mocks.getMySessions.mockResolvedValue([hintSession])
    mocks.getSession.mockResolvedValue(hintSession)

    const wrapper = mountView()
    await waitForAsyncUi()

    // Patterns list should be hidden initially
    expect(wrapper.find('.td-hint-card__patterns').exists()).toBe(false)
    expect(wrapper.text()).toContain('Could not detect intent')

    // Click "Show all patterns"
    const showBtn = wrapper.findAll('button').find((b) => b.text().includes('Show all patterns'))
    expect(showBtn).toBeTruthy()
    await showBtn!.trigger('click')
    await waitForAsyncUi()

    // Patterns should now be visible
    expect(wrapper.find('.td-hint-card__patterns').exists()).toBe(true)
    expect(wrapper.text()).toContain('archive card {id}')

    // Click "Hide all patterns"
    const hideBtn = wrapper.findAll('button').find((b) => b.text().includes('Hide all patterns'))
    expect(hideBtn).toBeTruthy()
    await hideBtn!.trigger('click')
    await waitForAsyncUi()

    expect(wrapper.find('.td-hint-card__patterns').exists()).toBe(false)
  })

  it('shows truncation notice instead of raw JSON for truncated assistant messages', async () => {
    const now = new Date().toISOString()
    const truncatedSession = {
      id: 'session-trunc',
      userId: 'user-1',
      boardId: null,
      title: 'Truncation test',
      status: 'Active',
      createdAt: now,
      updatedAt: now,
      recentMessages: [
        {
          id: 'msg-user',
          sessionId: 'session-trunc',
          role: 'User',
          content: 'Tell me about the board',
          messageType: 'text' as const,
          proposalId: null,
          tokenUsage: null,
          createdAt: now,
        },
        {
          id: 'msg-trunc',
          sessionId: 'session-trunc',
          role: 'Assistant',
          content: '{"reply":"I understand your question about',
          messageType: 'text' as const,
          proposalId: null,
          tokenUsage: 50,
          createdAt: now,
        },
      ],
    }
    mocks.getMySessions.mockResolvedValue([truncatedSession])
    mocks.getSession.mockResolvedValue(truncatedSession)

    const wrapper = mountView()
    await waitForAsyncUi()

    const truncatedMsg = wrapper.find('.td-message-content--truncated')
    expect(truncatedMsg.exists()).toBe(true)
    expect(truncatedMsg.text()).toContain('This response was cut short')
    expect(wrapper.text()).not.toContain('{"reply"')
  })

  it('shows truncation notice for degraded messages with truncated JSON content', async () => {
    const now = new Date().toISOString()
    const degradedTruncSession = {
      id: 'session-deg-trunc',
      userId: 'user-1',
      boardId: null,
      title: 'Degraded truncation test',
      status: 'Active',
      createdAt: now,
      updatedAt: now,
      recentMessages: [
        {
          id: 'msg-user',
          sessionId: 'session-deg-trunc',
          role: 'User',
          content: 'Hello',
          messageType: 'text' as const,
          proposalId: null,
          tokenUsage: null,
          createdAt: now,
        },
        {
          id: 'msg-deg-trunc',
          sessionId: 'session-deg-trunc',
          role: 'Assistant',
          content: '{"reply":"I understand',
          messageType: 'degraded' as const,
          proposalId: null,
          tokenUsage: 10,
          createdAt: now,
          degradedReason: 'Response was truncated',
        },
      ],
    }
    mocks.getMySessions.mockResolvedValue([degradedTruncSession])
    mocks.getSession.mockResolvedValue(degradedTruncSession)

    const wrapper = mountView()
    await waitForAsyncUi()

    expect(wrapper.text()).toContain('Degraded response: Response was truncated')
    expect(wrapper.find('.td-message-content--truncated').exists()).toBe(true)
    expect(wrapper.text()).toContain('This response was cut short')
    expect(wrapper.text()).not.toContain('{"reply"')
  })

  it('detects truncated JSON arrays starting with [', async () => {
    const now = new Date().toISOString()
    const arrayTruncSession = {
      id: 'session-arr-trunc',
      userId: 'user-1',
      boardId: null,
      title: 'Array truncation test',
      status: 'Active',
      createdAt: now,
      updatedAt: now,
      recentMessages: [
        {
          id: 'msg-arr-trunc',
          sessionId: 'session-arr-trunc',
          role: 'Assistant',
          content: '[{"id":1,"name":"incomplete',
          messageType: 'text' as const,
          proposalId: null,
          tokenUsage: 30,
          createdAt: now,
        },
      ],
    }
    mocks.getMySessions.mockResolvedValue([arrayTruncSession])
    mocks.getSession.mockResolvedValue(arrayTruncSession)

    const wrapper = mountView()
    await waitForAsyncUi()

    expect(wrapper.find('.td-message-content--truncated').exists()).toBe(true)
    expect(wrapper.text()).toContain('This response was cut short')
  })

  it('surfaces provider-health loading failures explicitly', async () => {
    mocks.getHealth.mockRejectedValueOnce(new Error('health down'))

    const wrapper = mountView()
    await waitForAsyncUi()

    expect(wrapper.text()).toContain('LLM status unavailable')
    expect(wrapper.text()).toContain('health down')
    expect(mocks.errorToast).toHaveBeenCalledWith('health down')
  })

  it('shows tool call metadata expander on messages with tool call data', async () => {
    const now = new Date().toISOString()
    const toolMetadata = JSON.stringify({
      rounds: 2,
      total_tokens: 4200,
      tool_calls: [
        { round: 1, tool: 'list_cards_in_column', args: { column_name: 'Done' }, result_summary: '3 cards found', is_error: false },
        { round: 2, tool: 'propose_bulk_move', args: { source_column: 'Done', target_column: 'Archive' }, result_summary: 'Proposal created', is_error: false },
      ],
    })
    const toolSession = {
      id: 'session-tool',
      userId: 'user-1',
      boardId: 'board-1',
      title: 'Tool test',
      status: 'Active',
      createdAt: now,
      updatedAt: now,
      recentMessages: [
        {
          id: 'msg-tool',
          sessionId: 'session-tool',
          role: 'Assistant',
          content: 'I created a proposal to move 3 cards.',
          messageType: 'text' as const,
          proposalId: null,
          tokenUsage: 4200,
          createdAt: now,
          toolCallMetadataJson: toolMetadata,
        },
      ],
    }
    mocks.getMySessions.mockResolvedValue([toolSession])
    mocks.getSession.mockResolvedValue(toolSession)

    const wrapper = mountView()
    await waitForAsyncUi()

    // Should show the tool meta toggle
    const toggle = wrapper.find('.td-tool-meta__toggle')
    expect(toggle.exists()).toBe(true)
    expect(toggle.text()).toContain('2 tool calls in 2 rounds')

    // Details should be hidden initially
    expect(wrapper.find('.td-tool-meta__details').exists()).toBe(false)

    // Click to expand
    await toggle.trigger('click')
    await waitForAsyncUi()

    const details = wrapper.find('.td-tool-meta__details')
    expect(details.exists()).toBe(true)
    expect(details.text()).toContain('List Cards In Column')
    expect(details.text()).toContain('Propose Bulk Move')
    expect(details.text()).toContain('Proposal')
  })

  it('does not show tool metadata expander for messages without tool call data', async () => {
    const wrapper = mountView()
    await waitForAsyncUi()

    expect(wrapper.find('.td-tool-meta').exists()).toBe(false)
  })

  it('shows tool status spinner while sending message', async () => {
    const sendDeferred = createDeferred<void>()
    mocks.sendMessage.mockReturnValueOnce(sendDeferred.promise)

    const wrapper = mountView()
    await waitForAsyncUi()

    const textarea = wrapper.find('textarea')
    await textarea.setValue('Move all done cards to archive')
    await findButtonByText(wrapper, 'Send Message').trigger('click')

    // The spinner should appear while sending
    await waitForAsyncUi()
    const statusMsg = wrapper.find('.td-message--tool-status')
    expect(statusMsg.exists()).toBe(true)
    expect(statusMsg.text()).toContain('Processing your request')

    // Resolve the send
    sendDeferred.resolve()
    await waitForAsyncUi()
  })
})
