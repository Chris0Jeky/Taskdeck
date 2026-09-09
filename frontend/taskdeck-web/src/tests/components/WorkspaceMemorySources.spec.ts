import { beforeEach, describe, expect, it, vi } from 'vitest'
import { reactive } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import WorkspaceMemorySources from '../../components/workspace/WorkspaceMemorySources.vue'
import type { Memory, MemorySourceDetail } from '../../types/workspaceInsights'

const mocks = vi.hoisted(() => ({ load: vi.fn() }))
const session = reactive({ userId: 'u1', token: 'token' })
vi.mock('../../store/sessionStore', () => ({ useSessionStore: () => session }))
vi.mock('../../api/workspaceInsights', () => ({ workspaceInsightsApi: { getMemorySources: mocks.load } }))
const memory: Memory = { id: 'm1', boardId: 'b1', title: 'Title', text: 'Corrected', originalText: 'Original', originalEvidence: null, status: 'statement', archived: false, revision: 2, createdAt: '', history: [], sources: { captureId: 'capture1', answerAssetId: 'asset2', evidenceAssetId: null } }
const result: MemorySourceDetail = { id: 'capture1', boardId: 'b1', capture: { sourceAssets: [
  { id: 'asset1', ordinal: 0, originalName: 'original.txt', contentHash: 'digest1', text: '<script>Original</script>', supersedesAssetId: null, supersededByAssetId: 'asset2' },
  { id: 'asset2', ordinal: 1, originalName: 'correction.txt', contentHash: 'digest2', text: 'Corrected', supersedesAssetId: 'asset1', supersededByAssetId: null },
] } }

describe('private memory originals', () => {
  beforeEach(() => { vi.clearAllMocks(); session.userId = 'u1'; session.token = 'token'; mocks.load.mockResolvedValue(result) })
  it('loads only on request and renders immutable originals as text with retention explained', async () => {
    const wrapper = mount(WorkspaceMemorySources, { props: { memory } })
    expect(mocks.load).not.toHaveBeenCalled()
    await wrapper.get('button').trigger('click'); await flushPromises()
    expect(wrapper.findAll('pre').map(item => item.text())).toEqual(['<script>Original</script>', 'Corrected'])
    expect(wrapper.find('script').exists()).toBe(false)
    expect(wrapper.text()).toContain('Superseded; original preserved')
    expect(wrapper.text()).toContain('Account deletion erases them')
    wrapper.unmount()
  })
  it('discards a private result after account changes and hides previously loaded text', async () => {
    let resolve!: (value: MemorySourceDetail) => void
    mocks.load.mockReturnValueOnce(new Promise<MemorySourceDetail>(done => { resolve = done }))
    const wrapper = mount(WorkspaceMemorySources, { props: { memory } })
    await wrapper.get('button').trigger('click')
    session.userId = 'u2'
    resolve(result); await flushPromises()
    expect(wrapper.find('pre').exists()).toBe(false)
    session.userId = 'u1'
    await wrapper.get('button').trigger('click'); await flushPromises()
    expect(wrapper.find('pre').exists()).toBe(true)
    session.token = ''
    await flushPromises()
    expect(wrapper.find('pre').exists()).toBe(false)
    wrapper.unmount()
  })
  it('clears originals on revision change and refuses mismatched source identity with retry available', async () => {
    const wrapper = mount(WorkspaceMemorySources, { props: { memory } })
    await wrapper.get('button').trigger('click'); await flushPromises()
    await wrapper.setProps({ memory: { ...memory, revision: 3 } })
    expect(wrapper.find('pre').exists()).toBe(false)
    mocks.load.mockResolvedValueOnce({ ...result, id: 'another-capture' })
    await wrapper.get('button').trigger('click'); await flushPromises()
    expect(wrapper.get('[role=alert]').text()).toContain('no longer match')
    expect(wrapper.get('button').attributes('disabled')).toBeUndefined()
    expect(wrapper.find('pre').exists()).toBe(false)
    wrapper.unmount()
  })
})
