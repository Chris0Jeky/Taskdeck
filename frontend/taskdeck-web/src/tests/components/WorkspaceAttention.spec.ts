import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { reactive } from 'vue'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import WorkspaceAttentionReminder from '../../components/workspace/WorkspaceAttentionReminder.vue'
import WorkspaceAttentionSettings from '../../components/workspace/WorkspaceAttentionSettings.vue'
import { workspaceAttentionApi } from '../../api/workspaceAttentionApi'
import { useWorkspaceAttentionStore } from '../../store/workspaceAttentionStore'

const session = reactive({ userId: 'owner', isAuthenticated: true, isDemo: false })
const route = reactive({ name: 'workspace-board', params: { id: 'board' }, query: {} as Record<string, string> })
const layout = reactive({ presentation: 'studio' })
vi.mock('../../store/sessionStore', () => ({ useSessionStore: () => session }))
vi.mock('../../store/workspaceLayoutStore', () => ({ useWorkspaceLayoutStore: () => layout }))
vi.mock('vue-router', () => ({ useRoute: () => route, RouterLink: { template: '<a><slot /></a>' } }))
vi.mock('../../api/workspaceAttentionApi', () => ({ workspaceAttentionApi: { get: vi.fn(), save: vi.fn(), claim: vi.fn() } }))
const settings = { enabled: true, revision: 1, dailyLimit: 2, minimumSpacingMinutes: 120 }
let wrapper: ReturnType<typeof mount> | undefined
beforeEach(() => {
  vi.resetAllMocks(); vi.useFakeTimers(); vi.setSystemTime(new Date('2026-09-10T12:00:00Z')); setActivePinia(createPinia())
  session.userId = 'owner'; session.isAuthenticated = true; session.isDemo = false
  route.name = 'workspace-board'; route.params.id = 'board'; route.query = {}; layout.presentation = 'studio'
  vi.spyOn(document, 'hasFocus').mockReturnValue(true)
  vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible')
  vi.mocked(workspaceAttentionApi.get).mockResolvedValue(settings)
  vi.mocked(workspaceAttentionApi.claim).mockResolvedValue({ boardId: 'board', insightId: 'question' })
})
afterEach(() => { wrapper?.unmount(); wrapper = undefined; document.body.innerHTML = ''; vi.useRealTimers(); vi.restoreAllMocks() })
describe('optional quiet reminders', () => {
  it('waits for a quiet minute and keeps keyboard navigation usable', async () => {
    wrapper = mount(WorkspaceAttentionReminder, { attachTo: document.body })
    expect(workspaceAttentionApi.claim).not.toHaveBeenCalled()
    await vi.advanceTimersByTimeAsync(60_000)
    expect(workspaceAttentionApi.claim).toHaveBeenCalledExactlyOnceWith('board')
    expect(wrapper.find('[aria-label="Saved question reminder"]').exists()).toBe(true)
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true }))
    expect(wrapper.find('aside').exists()).toBe(true)
    await wrapper.get('button').trigger('click')
    await vi.advanceTimersByTimeAsync(60_000)
    expect(workspaceAttentionApi.claim).toHaveBeenCalledTimes(1)
  })
  it.each(['off', 'zen', 'focus', 'typing', 'dialog', 'hidden', 'demo'] as const)('suppresses %s without reserving a reminder', async reason => {
    if (reason === 'off') vi.mocked(workspaceAttentionApi.get).mockResolvedValue({ ...settings, enabled: false })
    if (reason === 'zen') layout.presentation = 'zen'
    if (reason === 'focus') route.query.focus = '1'
    if (reason === 'hidden') vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('hidden')
    if (reason === 'demo') session.isDemo = true
    if (reason === 'dialog') document.body.innerHTML = '<div role="dialog"></div>'
    if (reason === 'typing') { const input = document.createElement('textarea'); document.body.append(input); input.focus() }
    wrapper = mount(WorkspaceAttentionReminder, { attachTo: document.body })
    await vi.advanceTimersByTimeAsync(120_000)
    expect(workspaceAttentionApi.claim).not.toHaveBeenCalled()
    expect(wrapper.find('aside').exists()).toBe(false)
  })
  it.each(['identity', 'board', 'typing', 'dialog'] as const)('discards a reserved response after %s changes', async change => {
    let resolve!: (value: { boardId: string; insightId: string }) => void
    vi.mocked(workspaceAttentionApi.claim).mockReturnValue(new Promise(done => { resolve = done }))
    wrapper = mount(WorkspaceAttentionReminder, { attachTo: document.body })
    await vi.advanceTimersByTimeAsync(60_000)
    if (change === 'identity') session.userId = 'other'
    if (change === 'board') route.params.id = 'other-board'
    if (change === 'typing') document.dispatchEvent(new Event('input', { bubbles: true }))
    if (change === 'dialog') document.body.append(Object.assign(document.createElement('div'), { role: 'dialog' }))
    await flushPromises(); resolve({ boardId: 'board', insightId: 'question' }); await flushPromises()
    expect(wrapper.find('aside').exists()).toBe(false)
  })
  it('preserves off until explicit save and recovers an uncertain preference without retrying it', async () => {
    vi.mocked(workspaceAttentionApi.get).mockResolvedValue({ ...settings, enabled: false })
    wrapper = mount(WorkspaceAttentionSettings); await flushPromises()
    expect(workspaceAttentionApi.save).not.toHaveBeenCalled()
    vi.mocked(workspaceAttentionApi.save).mockRejectedValue(new Error('lost receipt'))
    await wrapper.get('input').setValue(true); await flushPromises()
    expect(workspaceAttentionApi.save).toHaveBeenCalledExactlyOnceWith(1, true)
    expect(useWorkspaceAttentionStore().settings).toBeNull()
    vi.mocked(workspaceAttentionApi.get).mockResolvedValue({ ...settings, revision: 2 })
    await wrapper.get('button').trigger('click'); await flushPromises()
    expect(useWorkspaceAttentionStore().settings?.enabled).toBe(true)
    expect(workspaceAttentionApi.save).toHaveBeenCalledTimes(1)
  })
})
