import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import WorkspaceMemoryExport from '../../components/workspace/WorkspaceMemoryExport.vue'

const mocks = vi.hoisted(() => ({
  collect: vi.fn(), download: vi.fn(), session: { userId: 'user-1', token: 'test-token' },
}))
vi.mock('../../store/sessionStore', () => ({ useSessionStore: () => mocks.session }))
vi.mock('../../utils/workspaceMemoryExport', () => ({ collectWorkspaceMemoryExport: mocks.collect, downloadWorkspaceMemoryJson: mocks.download }))
vi.mock('../../composables/useErrorMapper', () => ({ getErrorDisplay: (error: Error) => ({ message: error.message }) }))

describe('WorkspaceMemoryExport', () => {
  beforeEach(() => { vi.clearAllMocks(); mocks.collect.mockResolvedValue('{"memories":[]}'); mocks.download.mockReset() })
  it('exports only after a click and reports a download request rather than a saved file', async () => {
    const wrapper = mount(WorkspaceMemoryExport, { props: { boardId: 'board-1' } })
    expect(mocks.collect).not.toHaveBeenCalled()
    await wrapper.get('button').trigger('click')
    await flushPromises()
    expect(mocks.collect).toHaveBeenCalledWith('board-1', expect.any(Function))
    expect(mocks.download).toHaveBeenCalledWith('{"memories":[]}', 'board-1')
    expect(wrapper.get('[role=status]').text()).toContain('Download requested')
  })
  it('shows download failures, re-enables retry and does not emit draft mutations', async () => {
    mocks.download.mockImplementationOnce(() => { throw new Error('Download blocked') })
    const wrapper = mount(WorkspaceMemoryExport, { props: { boardId: 'board-1' } })
    await wrapper.get('button').trigger('click'); await flushPromises()
    expect(wrapper.get('[role=alert]').text()).toBe('Download blocked')
    expect(wrapper.get('button').attributes('disabled')).toBeUndefined()
    expect(wrapper.find('[role=status]').exists()).toBe(false)
    expect(wrapper.emitted()).not.toHaveProperty('update')
  })
  it('does not download for a board changed while collecting', async () => {
    let finish!: (json: string) => void
    mocks.collect.mockReturnValue(new Promise<string>(resolve => { finish = resolve }))
    const wrapper = mount(WorkspaceMemoryExport, { props: { boardId: 'board-1' } })
    await wrapper.get('button').trigger('click')
    await wrapper.setProps({ boardId: 'board-2' })
    finish('{}'); await flushPromises()
    expect(mocks.download).not.toHaveBeenCalled()
    expect(wrapper.get('[role=alert]').text()).toContain('changed')
  })
})
