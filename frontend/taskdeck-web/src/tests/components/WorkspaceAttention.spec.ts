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
  it('retains the hours draft after a definite validation rejection and saves the correction once', async () => {
    wrapper = mount(WorkspaceAttentionSettings); await flushPromises()
    await wrapper.findAll('input[type=checkbox]')[1]!.setValue(true)
    await wrapper.get('input[type=text]').setValue('Not/AZone')
    await wrapper.findAll('input[type=time]')[0]!.setValue('22:00')
    await wrapper.findAll('input[type=time]')[1]!.setValue('02:00')
    const message = 'Choose a valid IANA time zone and reminder hours.'
    vi.mocked(workspaceAttentionApi.save).mockRejectedValueOnce({ response: { status: 400, data: { errorCode: 'ValidationError', message } } })
    await wrapper.get('form').trigger('submit'); await flushPromises()
    expect(wrapper.find('form').exists()).toBe(true)
    expect((wrapper.get('input[type=text]').element as HTMLInputElement).value).toBe('Not/AZone')
    expect(wrapper.text()).toContain(message)
    expect(useWorkspaceAttentionStore().settings?.revision).toBe(1)
    expect(workspaceAttentionApi.save).toHaveBeenCalledTimes(1)
    await wrapper.get('input[type=text]').setValue('UTC')
    const window = { timeZoneId: 'UTC', daysMask: 62, startMinute: 1320, endMinute: 120 }
    vi.mocked(workspaceAttentionApi.save).mockResolvedValueOnce({ ...settings, revision: 2, window })
    await wrapper.get('form').trigger('submit'); await flushPromises()
    expect(workspaceAttentionApi.save).toHaveBeenCalledTimes(2)
    expect(workspaceAttentionApi.save).toHaveBeenLastCalledWith(1, true, window)
  })

  it.each([true, false])('preserves an hours draft across an enable-only save to %s', async enabled => {
    vi.mocked(workspaceAttentionApi.get).mockResolvedValue({ ...settings, enabled: !enabled })
    wrapper = mount(WorkspaceAttentionSettings); await flushPromises()
    await wrapper.findAll('input[type=checkbox]')[1]!.setValue(true)
    await wrapper.get('input[type=text]').setValue('America/New_York')
    await wrapper.findAll('input[type=checkbox]')[4]!.setValue(false)
    await wrapper.findAll('input[type=checkbox]')[7]!.setValue(true)
    await wrapper.findAll('input[type=time]')[0]!.setValue('22:00')
    await wrapper.findAll('input[type=time]')[1]!.setValue('02:00')
    vi.mocked(workspaceAttentionApi.save).mockResolvedValue({ ...settings, enabled, revision: 2 })
    await wrapper.get('input').setValue(enabled); await flushPromises()
    expect(workspaceAttentionApi.save).toHaveBeenCalledExactlyOnceWith(1, enabled)
    expect((wrapper.findAll('input[type=checkbox]')[1]!.element as HTMLInputElement).checked).toBe(true)
    const window = { timeZoneId: 'America/New_York', daysMask: 118, startMinute: 1320, endMinute: 120 }
    vi.mocked(workspaceAttentionApi.save).mockResolvedValue({ ...settings, enabled, revision: 3, window })
    await wrapper.get('form').trigger('submit'); await flushPromises()
    expect(workspaceAttentionApi.save).toHaveBeenCalledTimes(2)
    expect(workspaceAttentionApi.save).toHaveBeenLastCalledWith(2, enabled, window)
  })

  it('clears an hours draft on account change while ignoring the old enable receipt', async () => {
    wrapper = mount(WorkspaceAttentionSettings); await flushPromises()
    await wrapper.findAll('input[type=checkbox]')[1]!.setValue(true)
    await wrapper.get('input[type=text]').setValue('America/New_York')
    let complete!: (value: typeof settings) => void
    vi.mocked(workspaceAttentionApi.save).mockReturnValue(new Promise(resolve => { complete = resolve }))
    await wrapper.get('input').setValue(false); await flushPromises()
    session.userId = 'different-owner'; await flushPromises()
    expect(wrapper.find('form').exists()).toBe(false)
    const window = { timeZoneId: 'Europe/London', daysMask: 62, startMinute: 540, endMinute: 1020 }
    vi.mocked(workspaceAttentionApi.get).mockResolvedValue({ ...settings, window })
    await wrapper.get('button').trigger('click'); await flushPromises()
    complete({ ...settings, enabled: false, revision: 2 }); await flushPromises()
    expect((wrapper.get('input[type=text]').element as HTMLInputElement).value).toBe('Europe/London')
    expect(useWorkspaceAttentionStore().settings?.enabled).toBe(true)
  })

  it.each(['resolve', 'reject'])('keeps the newer account save guarded when the old save settles by %s', async outcome => {
    wrapper = mount(WorkspaceAttentionSettings); await flushPromises()
    let completeOld!: (value: typeof settings) => void
    let rejectOld!: (reason: Error) => void
    vi.mocked(workspaceAttentionApi.save).mockReturnValueOnce(new Promise((resolve, reject) => {
      completeOld = resolve; rejectOld = reject
    }))
    await wrapper.get('input').setValue(false); await flushPromises()
    session.userId = 'different-owner'; await flushPromises()
    const savedWindow = { timeZoneId: 'Europe/London', daysMask: 62, startMinute: 540, endMinute: 1020 }
    vi.mocked(workspaceAttentionApi.get).mockResolvedValueOnce({ ...settings, revision: 10, window: savedWindow })
    await wrapper.get('button').trigger('click'); await flushPromises()
    await wrapper.get('input[type=text]').setValue('America/New_York')
    await wrapper.findAll('input[type=time]')[0]!.setValue('22:00')
    await wrapper.findAll('input[type=time]')[1]!.setValue('02:00')
    let completeNew!: (value: typeof settings & { window: typeof savedWindow }) => void
    vi.mocked(workspaceAttentionApi.save).mockReturnValueOnce(new Promise(resolve => { completeNew = resolve }))
    await wrapper.get('input').setValue(false); await flushPromises()
    if (outcome === 'resolve') completeOld({ ...settings, enabled: false, revision: 2 })
    else rejectOld(new Error('Old account save response was lost'))
    await flushPromises()
    expect(useWorkspaceAttentionStore().busy).toBe(true)
    expect(useWorkspaceAttentionStore().settings?.revision).toBe(10)
    completeNew({ ...settings, enabled: false, revision: 11, window: savedWindow }); await flushPromises()
    expect((wrapper.get('input[type=text]').element as HTMLInputElement).value).toBe('America/New_York')
    expect((wrapper.findAll('input[type=time]')[0]!.element as HTMLInputElement).value).toBe('22:00')
    expect((wrapper.findAll('input[type=time]')[1]!.element as HTMLInputElement).value).toBe('02:00')
    const window = { timeZoneId: 'America/New_York', daysMask: 62, startMinute: 1320, endMinute: 120 }
    vi.mocked(workspaceAttentionApi.save).mockResolvedValueOnce({ ...settings, enabled: false, revision: 12, window })
    await wrapper.get('form').trigger('submit'); await flushPromises()
    expect(workspaceAttentionApi.save).toHaveBeenCalledTimes(3)
    expect(workspaceAttentionApi.save).toHaveBeenLastCalledWith(11, false, window)
  })

  it('saves an explicit weekly window and disables controls until its receipt arrives', async () => {
    wrapper = mount(WorkspaceAttentionSettings); await flushPromises()
    const restrict = wrapper.findAll('input[type=checkbox]')[1]!
    await restrict.setValue(true)
    await wrapper.get('input[type=text]').setValue('America/New_York')
    await wrapper.findAll('input[type=time]')[0]!.setValue('22:00')
    await wrapper.findAll('input[type=time]')[1]!.setValue('02:00')
    const window = { timeZoneId: 'America/New_York', daysMask: 62, startMinute: 1320, endMinute: 120 }
    let complete!: (value: typeof settings & { window: typeof window }) => void
    vi.mocked(workspaceAttentionApi.save).mockReturnValue(new Promise(resolve => { complete = resolve }))
    await wrapper.get('form').trigger('submit'); await flushPromises()
    expect(workspaceAttentionApi.save).toHaveBeenCalledExactlyOnceWith(1, true, window)
    expect(wrapper.get('fieldset').attributes('disabled')).toBeDefined()
    complete({ ...settings, revision: 2, window }); await flushPromises()
    expect(wrapper.get('fieldset').attributes('disabled')).toBeUndefined()
    expect(useWorkspaceAttentionStore().settings?.window).toEqual(window)
  })

  it('clears a saved window explicitly and recovers uncertain completion from the server', async () => {
    const window = { timeZoneId: 'Europe/London', daysMask: 62, startMinute: 540, endMinute: 1020 }
    vi.mocked(workspaceAttentionApi.get).mockResolvedValue({ ...settings, window })
    wrapper = mount(WorkspaceAttentionSettings); await flushPromises()
    await wrapper.findAll('input[type=checkbox]')[1]!.setValue(false)
    vi.mocked(workspaceAttentionApi.save).mockRejectedValue(new Error('lost save receipt'))
    await wrapper.get('form').trigger('submit'); await flushPromises()
    expect(workspaceAttentionApi.save).toHaveBeenCalledExactlyOnceWith(1, true, null)
    expect(wrapper.find('form').exists()).toBe(false)
    expect(useWorkspaceAttentionStore().settings).toBeNull()
    vi.mocked(workspaceAttentionApi.get).mockResolvedValue({ ...settings, revision: 2, window: null })
    await wrapper.get('button').trigger('click'); await flushPromises()
    expect(useWorkspaceAttentionStore().settings?.window).toBeNull()
    expect(workspaceAttentionApi.save).toHaveBeenCalledTimes(1)
  })

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
  it.each(['identity', 'board', 'typing', 'dialog', 'window'] as const)('discards a reserved response after %s changes', async change => {
    let resolve!: (value: { boardId: string; insightId: string }) => void
    vi.mocked(workspaceAttentionApi.claim).mockReturnValue(new Promise(done => { resolve = done }))
    wrapper = mount(WorkspaceAttentionReminder, { attachTo: document.body })
    await vi.advanceTimersByTimeAsync(60_000)
    if (change === 'identity') session.userId = 'other'
    if (change === 'board') route.params.id = 'other-board'
    if (change === 'typing') document.dispatchEvent(new Event('input', { bubbles: true }))
    if (change === 'dialog') document.body.append(Object.assign(document.createElement('div'), { role: 'dialog' }))
    if (change === 'window') useWorkspaceAttentionStore().settings = { ...settings, window: { timeZoneId: 'UTC', daysMask: 2, startMinute: 540, endMinute: 1020 } }
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
