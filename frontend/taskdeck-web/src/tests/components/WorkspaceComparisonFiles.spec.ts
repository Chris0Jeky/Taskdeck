import { beforeEach, describe, expect, it, vi } from 'vitest'
import { reactive } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import WorkspaceComparisonFiles from '../../components/workspace/WorkspaceComparisonFiles.vue'

const mocks = vi.hoisted(() => ({ importJson: vi.fn() }))
const session = reactive({ userId: 'u1' as string | null })
vi.mock('../../store/sessionStore', () => ({ useSessionStore: () => session }))
vi.mock('../../store/workspaceExperimentStore', () => ({
  MAX_COMPARISON_FILE_BYTES: 2 * 1024 * 1024,
  WORKSPACE_COMPARISON_SCENARIOS: [],
  useWorkspaceExperimentStore: () => ({ importJson: mocks.importJson, groups: [] }),
}))

describe('portable comparison files', () => {
  beforeEach(() => { vi.clearAllMocks(); session.userId = 'u1'; mocks.importJson.mockResolvedValue(1) })
  it('imports explicitly and reports validation failures without claiming success', async () => {
    const wrapper = mount(WorkspaceComparisonFiles)
    expect(mocks.importJson).not.toHaveBeenCalled()
    const input = wrapper.get('input')
    Object.defineProperty(input.element, 'files', { configurable: true, value: [{ size: 2, text: () => Promise.resolve('{}') }] })
    await input.trigger('change'); await flushPromises()
    expect(mocks.importJson).toHaveBeenCalledWith('{}')
    expect(wrapper.get('[role=status]').text()).toContain('Imported 1')
    mocks.importJson.mockRejectedValueOnce(new Error('Invalid comparison file'))
    await input.trigger('change'); await flushPromises()
    expect(wrapper.get('[role=alert]').text()).toBe('Invalid comparison file')
    expect(wrapper.find('[role=status]').exists()).toBe(false)
    wrapper.unmount()
  })
  it('rejects oversized files before reading their content', async () => {
    const wrapper = mount(WorkspaceComparisonFiles)
    const input = wrapper.get('input')
    const read = vi.fn()
    Object.defineProperty(input.element, 'files', { value: [{ size: 2 * 1024 * 1024 + 1, text: read }] })
    await input.trigger('change'); await flushPromises()
    expect(read).not.toHaveBeenCalled()
    expect(mocks.importJson).not.toHaveBeenCalled()
    expect(wrapper.get('[role=alert]').text()).toContain('2 MiB')
    wrapper.unmount()
  })
  it.each(['account', 'unmount'])('does not import a file after %s changes during reading', async change => {
    const wrapper = mount(WorkspaceComparisonFiles)
    const input = wrapper.get('input')
    let resolve!: (json: string) => void
    Object.defineProperty(input.element, 'files', { value: [{ size: 2, text: () => new Promise<string>(done => { resolve = done }) }] })
    await input.trigger('change')
    if (change === 'account') session.userId = 'u2'
    else wrapper.unmount()
    resolve('{}'); await flushPromises()
    expect(mocks.importJson).not.toHaveBeenCalled()
    if (change === 'account') wrapper.unmount()
  })
})
