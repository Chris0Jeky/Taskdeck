import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import ChatOriginalSourcePicker from '../../../components/chat/ChatOriginalSourcePicker.vue'
import { useSessionStore } from '../../../store/sessionStore'
const api = vi.hoisted(() => ({ list: vi.fn() }))
vi.mock('../../../api/chatSourcesApi', () => ({ chatSourcesApi: api }))
const asset = { id: 'a1', name: 'answer-revision-1.txt', contentHash: 'a'.repeat(64), byteSize: 23, supersededByAssetId: 'a2', excerpt: '<script>old source</script>', truncated: false }
const page = () => ({ memoryId: 'm1', revision: 2, items: [asset], nextOffset: null })
function setup() {
  const pinia = createPinia(); setActivePinia(pinia)
  const session = useSessionStore(); session.userId = 'owner'; session.token = 'token'
  return { session, wrapper: mount(ChatOriginalSourcePicker, { props: { memoryId: 'm1', boardId: 'b1', revision: 2, selected: [], selectedCount: 0 }, global: { plugins: [pinia] } }) }
}
describe('original source choice', () => {
  beforeEach(() => { vi.clearAllMocks(); api.list.mockResolvedValue(page()) })
  it('loads only on request, escapes evidence and emits identity without source text', async () => {
    const { wrapper } = setup(); expect(api.list).not.toHaveBeenCalled()
    await wrapper.get('button').trigger('click'); await flushPromises()
    expect(api.list).toHaveBeenCalledWith('m1', 'b1', 2, 0)
    expect(wrapper.text()).toContain('Superseded answer'); expect(wrapper.find('script').exists()).toBe(false)
    await wrapper.get('input').setValue(true)
    expect(wrapper.emitted('change')?.at(-1)).toEqual([[{ memoryId: 'm1', revision: 2, assetId: 'a1', contentHash: asset.contentHash }]])
  })
  it.each(['owner', 'revision', 'unmount'])('discards a late read after %s changes', async reason => {
    let resolve!: (value: ReturnType<typeof page>) => void
    api.list.mockReturnValue(new Promise(done => { resolve = done }))
    const { wrapper, session } = setup(); await wrapper.get('button').trigger('click')
    if (reason === 'owner') session.userId = 'other'
    else if (reason === 'revision') await wrapper.setProps({ revision: 3 })
    else wrapper.unmount()
    resolve(page()); await flushPromises()
    expect(wrapper.text()).not.toContain('old source')
  })
  it('rejects mismatched receipts and permits a bounded retry', async () => {
    api.list.mockResolvedValueOnce({ ...page(), revision: 1 })
    const { wrapper } = setup(); await wrapper.get('button').trigger('click'); await flushPromises()
    expect(wrapper.get('[role="alert"]').text()).toContain('could not be checked')
    expect(wrapper.find('input').exists()).toBe(false)
    await wrapper.get('button').trigger('click'); await flushPromises()
    expect(wrapper.find('input').exists()).toBe(true)
  })
  it('shares the five-item limit and allows deselection at the limit', async () => {
    const { wrapper } = setup(); await wrapper.get('button').trigger('click'); await flushPromises()
    await wrapper.setProps({ selectedCount: 5 })
    expect(wrapper.get('input').attributes('disabled')).toBeDefined()
    await wrapper.setProps({ selected: [{ memoryId: 'm1', revision: 2, assetId: 'a1', contentHash: asset.contentHash }] })
    expect(wrapper.get('input').attributes('disabled')).toBeUndefined()
    await wrapper.get('input').setValue(false)
    expect(wrapper.emitted('change')?.at(-1)).toEqual([[]])
  })
  it('loads subsequent pages without duplicating sources', async () => {
    api.list.mockResolvedValueOnce({ ...page(), nextOffset: 10 }).mockResolvedValueOnce({ ...page(), items: [asset, { ...asset, id: 'a2' }] })
    const { wrapper } = setup(); await wrapper.get('button').trigger('click'); await flushPromises()
    await wrapper.get('button').trigger('click'); await flushPromises()
    expect(api.list).toHaveBeenLastCalledWith('m1', 'b1', 2, 10)
    expect(wrapper.findAll('input')).toHaveLength(2)
  })
  it('names each picker by its memory and clears prior selections after revoked access', async () => {
    api.list.mockResolvedValueOnce({ ...page(), nextOffset: 10 }).mockRejectedValueOnce({ response: { status: 403 } })
    const { wrapper } = setup(); await wrapper.setProps({ memoryTitle: 'Release lesson' })
    expect(wrapper.get('button').attributes('aria-label')).toBe('Choose original sources for Release lesson')
    await wrapper.get('button').trigger('click'); await flushPromises()
    expect(wrapper.get('button').attributes('aria-label')).toBe('Load more originals for Release lesson')
    await wrapper.get('button').trigger('click'); await flushPromises()
    expect(wrapper.get('[role=alert]').text()).toContain('no longer have access')
    expect(wrapper.find('input').exists()).toBe(false); expect(wrapper.emitted('change')?.at(-1)).toEqual([[]])
  })
  it('preserves a pending source read during same-user token refresh but clears on logout', async () => {
    let resolve!: (value: ReturnType<typeof page>) => void
    api.list.mockReturnValueOnce(new Promise(done => { resolve = done }))
    const { wrapper, session } = setup(); await wrapper.get('button').trigger('click')
    session.token = 'refreshed'; resolve(page()); await flushPromises()
    expect(wrapper.find('input').exists()).toBe(true)
    session.token = ''; await flushPromises(); expect(wrapper.find('input').exists()).toBe(false)
  })
})
