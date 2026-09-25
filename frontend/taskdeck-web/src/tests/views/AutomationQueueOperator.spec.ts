import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { reactive } from 'vue'
import AutomationQueueView from '../../views/AutomationQueueView.vue'

const session = reactive({ defaultRole: null as number | null, isAuthenticated: true })
const queue = vi.hoisted(() => ({
  stats: null,
  loading: false,
  requests: [],
  fetchByStatus: vi.fn().mockResolvedValue(undefined),
  fetchStats: vi.fn().mockResolvedValue(undefined),
  processNext: vi.fn().mockResolvedValue(undefined),
}))
vi.mock('../../store/sessionStore', () => ({ useSessionStore: () => session }))
vi.mock('../../store/queueStore', () => ({ useQueueStore: () => queue }))
vi.mock('../../store/toastStore', () => ({
  useToastStore: () => ({ error: vi.fn(), success: vi.fn() }),
}))
vi.mock('../../api/boardsApi', () => ({
  boardsApi: { getBoards: vi.fn().mockResolvedValue([]) },
}))
vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn() }) }))

function processButton(wrapper: ReturnType<typeof mount>) {
  return wrapper.findAll('button').find((button) => button.text() === 'Process Next')
}

describe('Automation queue operator boundary', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    session.defaultRole = null
    session.isAuthenticated = true
  })

  it.each([null, 2, 3, -1, 4])('hides global claiming for unknown or ordinary role %s', async (role) => {
    session.defaultRole = role
    const wrapper = mount(AutomationQueueView)
    await flushPromises()
    expect(processButton(wrapper)).toBeUndefined()
    expect(queue.processNext).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it.each([0, 1])('keeps global claiming usable for operator role %s', async (role) => {
    session.defaultRole = role
    const wrapper = mount(AutomationQueueView)
    await flushPromises()
    const button = processButton(wrapper)
    expect(button).toBeDefined()
    await button!.trigger('click')
    await flushPromises()
    expect(queue.processNext).toHaveBeenCalledTimes(1)
    wrapper.unmount()
  })

  it('hides claiming for an unauthenticated session even with a stale operator role', async () => {
    session.defaultRole = 1
    session.isAuthenticated = false
    const wrapper = mount(AutomationQueueView)
    await flushPromises()
    expect(processButton(wrapper)).toBeUndefined()
    wrapper.unmount()
  })

  it.each(['role loss', 'logout'])('rejects an old button click immediately after %s', async (transition) => {
    session.defaultRole = 1
    const wrapper = mount(AutomationQueueView)
    await flushPromises()
    const button = processButton(wrapper)
    expect(button).toBeDefined()
    if (transition === 'role loss') session.defaultRole = 2
    else session.isAuthenticated = false
    // Dispatch synchronously, before Vue removes the old DOM in its next render.
    button!.element.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    await flushPromises()
    expect(queue.processNext).not.toHaveBeenCalled()
    expect(processButton(wrapper)).toBeUndefined()
    wrapper.unmount()
  })
})
