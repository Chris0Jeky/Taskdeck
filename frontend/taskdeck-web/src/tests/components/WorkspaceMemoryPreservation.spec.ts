import { beforeEach, describe, expect, it, vi } from 'vitest'
import { reactive } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import WorkspaceMemoryPreservation from '../../components/workspace/WorkspaceMemoryPreservation.vue'
import type { Memory } from '../../types/workspaceInsights'

const mocks = vi.hoisted(() => ({ preserve: vi.fn() }))
const session = reactive({ userId: 'u1', token: 'token' })
vi.mock('../../store/sessionStore', () => ({ useSessionStore: () => session }))
vi.mock('../../api/workspaceInsights', () => ({ workspaceInsightsApi: { preserveMemorySources: mocks.preserve } }))
const memory = (id = 'm1'): Memory => ({ id, boardId: 'b1', title: 'Title', text: 'Original', originalText: 'Original', originalEvidence: null, status: 'unknown', archived: false, revision: 1, createdAt: '', history: [], sources: null })
const saved = (id = 'm1'): Memory => ({ ...memory(id), revision: 2, sources: { captureId: 'capture1', answerAssetId: 'asset1', evidenceAssetId: null } })

describe('preserve older memory originals', () => {
  beforeEach(() => { vi.clearAllMocks(); session.userId = 'u1'; session.token = 'token'; mocks.preserve.mockResolvedValue([saved()]) })
  it('requires an explicit action and sends at most 50 visible unlinked memories with their revisions', async () => {
    const memories = Array.from({ length: 51 }, (_, index) => memory(`m${index}`))
    mocks.preserve.mockResolvedValue(memories.slice(0, 50).map(x => saved(x.id)))
    const wrapper = mount(WorkspaceMemoryPreservation, { props: { boardId: 'b1', memories: [...memories, saved('linked'), { ...memory('other'), boardId: 'b2' }] } })
    expect(mocks.preserve).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('Continue in batches of 50')
    await wrapper.get('button').trigger('click'); await flushPromises()
    expect(mocks.preserve).toHaveBeenCalledWith('b1', memories.slice(0, 50).map(({ id, revision }) => ({ id, revision })))
    expect(wrapper.emitted('preserved')?.[0]?.[0]).toHaveLength(50)
    wrapper.unmount()
  })
  it('surfaces a conflict and supports a retry without claiming partial success', async () => {
    mocks.preserve.mockRejectedValueOnce(new Error('The workspace changed. Reload and try again.'))
    const wrapper = mount(WorkspaceMemoryPreservation, { props: { boardId: 'b1', memories: [memory()] } })
    await wrapper.get('button').trigger('click'); await flushPromises()
    expect(wrapper.get('[role=alert]').text()).toContain('workspace changed')
    expect(wrapper.emitted('preserved')).toBeUndefined()
    await wrapper.get('button').trigger('click'); await flushPromises()
    expect(wrapper.emitted('preserved')).toHaveLength(1)
    wrapper.unmount()
  })
  it.each(['account', 'board', 'revision', 'unmount'])('discards a completed private result after %s changes', async change => {
    let resolve!: (value: Memory[]) => void
    mocks.preserve.mockReturnValueOnce(new Promise<Memory[]>(done => { resolve = done }))
    const wrapper = mount(WorkspaceMemoryPreservation, { props: { boardId: 'b1', memories: [memory()] } })
    await wrapper.get('button').trigger('click')
    if (change === 'account') session.userId = 'u2'
    if (change === 'board') await wrapper.setProps({ boardId: 'b2' })
    if (change === 'revision') await wrapper.setProps({ memories: [{ ...memory(), revision: 2 }] })
    if (change === 'unmount') wrapper.unmount()
    resolve([saved()]); await flushPromises()
    expect(wrapper.emitted('preserved')).toBeUndefined()
    if (change !== 'unmount') wrapper.unmount()
  })
  it('rejects mismatched results and disables writes while editing', async () => {
    const wrapper = mount(WorkspaceMemoryPreservation, { props: { boardId: 'b1', memories: [memory()], disabled: true } })
    await wrapper.get('button').trigger('click')
    expect(mocks.preserve).not.toHaveBeenCalled()
    await wrapper.setProps({ disabled: false })
    mocks.preserve.mockResolvedValueOnce([{ ...saved(), boardId: 'b2' }])
    await wrapper.get('button').trigger('click'); await flushPromises()
    expect(wrapper.get('[role=alert]').text()).toContain('Reload memory')
    expect(wrapper.emitted('preserved')).toBeUndefined()
    wrapper.unmount()
  })
})
