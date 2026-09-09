import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import ChatProposalPreview from '../../../components/chat/ChatProposalPreview.vue'
import { useSessionStore } from '../../../store/sessionStore'
import type { ProposalPreview } from '../../../types/automation'
const api = vi.hoisted(() => ({ preview: vi.fn() }))
vi.mock('../../../api/automationApi', () => ({ automationApi: { getProposalPreview: api.preview } }))
const snapshot = (): ProposalPreview => ({ proposalId: 'p1', boardId: 'b1', status: 'PendingReview', effectiveRevisionId: 'r2', effectiveRevisionNumber: 2, proposalUpdatedAt: new Date().toISOString(), expiresAt: new Date(Date.now() + 60000).toISOString(), checkedAt: new Date().toISOString(), diff: 'Update card: revised title <script>unsafe</script>' })
function setup() {
  const pinia = createPinia(); setActivePinia(pinia)
  return { session: useSessionStore(), wrapper: mount(ChatProposalPreview, { props: { proposalId: 'p1', boardId: 'b1' }, global: { plugins: [pinia] } }) }
}
describe('contextual proposal preview', () => {
  beforeEach(() => { vi.clearAllMocks(); api.preview.mockResolvedValue(snapshot()) })
  afterEach(() => vi.useRealTimers())
  it('shows one authoritative revision receipt as text and offers no mutation', async () => {
    const { wrapper } = setup(); expect(api.preview).not.toHaveBeenCalled()
    await wrapper.get('button').trigger('click'); await flushPromises()
    expect(wrapper.text()).toContain('Revision 2')
    expect(wrapper.get('pre').text()).toContain('<script>unsafe</script>')
    expect(wrapper.find('script').exists()).toBe(false)
    expect(wrapper.findAll('button')).toHaveLength(1)
    wrapper.unmount()
  })
  it('retracts the preview after its bounded freshness window', async () => {
    vi.useFakeTimers()
    const { wrapper } = setup(); await wrapper.get('button').trigger('click'); await flushPromises()
    await vi.advanceTimersByTimeAsync(30000)
    expect(wrapper.find('pre').exists()).toBe(false)
    expect(wrapper.text()).toContain('Refresh the preview')
    wrapper.unmount()
  })
  it('rejects a different board and clears a revealed preview on identity change', async () => {
    api.preview.mockResolvedValueOnce({ ...snapshot(), boardId: 'other' })
    const { wrapper, session } = setup(); await wrapper.get('button').trigger('click'); await flushPromises()
    expect(wrapper.find('pre').exists()).toBe(false)
    await wrapper.get('button').trigger('click'); await flushPromises()
    expect(wrapper.find('pre').exists()).toBe(true)
    session.userId = 'replacement'; await flushPromises()
    expect(wrapper.find('pre').exists()).toBe(false)
    wrapper.unmount()
  })
})
