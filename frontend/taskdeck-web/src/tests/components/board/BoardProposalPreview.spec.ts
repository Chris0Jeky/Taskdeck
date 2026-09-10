import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { computed, defineComponent, provide, ref } from 'vue'
import BoardProposalPreview from '../../../components/board/BoardProposalPreview.vue'
import CardItem from '../../../components/board/CardItem.vue'
import PaperBoardCard from '../../../views/paper/PaperBoardCard.vue'
import { BOARD_PROPOSAL_MARKERS, type BoardProposalMarkers } from '../../../composables/useBoardProposalMarker'
import { useSessionStore } from '../../../store/sessionStore'
import type { BoardDetail, Card } from '../../../types/board'
import type { Proposal, ProposalPreview } from '../../../types/automation'

const api = vi.hoisted(() => ({ preview: vi.fn(), detail: vi.fn() }))
vi.mock('../../../api/automationApi', () => ({ automationApi: { getProposalPreview: api.preview, getProposal: api.detail } }))
const at = new Date().toISOString()
const board: BoardDetail = { id: 'b1', name: 'Board', description: null, isArchived: false, createdAt: at, updatedAt: at, columns: [{ id: 'c1', boardId: 'b1', name: 'Next', position: 0, wipLimit: null, cardCount: 1, createdAt: at, updatedAt: at }] }
const card: Card = { id: 'a1', boardId: 'b1', columnId: 'c1', title: 'Saved title', description: '', dueDate: null, isBlocked: false, blockReason: null, position: 0, labels: [], createdAt: at, updatedAt: at }
const receipt = (): ProposalPreview => ({ proposalId: 'p1', boardId: 'b1', status: 'PendingReview', effectiveRevisionId: 'r2', effectiveRevisionNumber: 2, proposalUpdatedAt: at, expiresAt: new Date(Date.now() + 60000).toISOString(), checkedAt: new Date().toISOString(), diff: 'Update Saved title → Proposed title <script>unsafe</script>' })
const detail = (): Proposal => ({ id: 'p1', boardId: 'b1', status: 'PendingReview', latestRevisionId: 'r2', approvedRevisionId: null, updatedAt: at, summary: 'Rename a card and column; create another card', operations: [
  { targetType: 'Card', targetId: 'wrong-display-target', actionType: 'update', parameters: '{"CardId":"A1"}' },
  { targetType: 'Column', targetId: 'c1', actionType: 'update' },
  { targetType: 'Card', targetId: null, actionType: 'create' },
  { targetType: 'Card', targetId: 'not-on-board', actionType: 'delete' },
] } as Proposal)
function setup() {
  const pinia = createPinia(); setActivePinia(pinia)
  const session = useSessionStore(); session.userId = 'user'; session.token = 'token'
  const wrapper = mount(BoardProposalPreview, { props: { proposalId: 'p1', board: structuredClone(board), cards: [structuredClone(card)] }, global: { plugins: [pinia], stubs: { RouterLink: true } } })
  const load = async () => { await wrapper.get('button').trigger('click'); await flushPromises() }
  return { wrapper, load, session }
}
describe('board proposal preview', () => {
  beforeEach(() => { vi.clearAllMocks(); api.preview.mockResolvedValue(receipt()); api.detail.mockResolvedValue(detail()) })
  afterEach(() => { vi.useRealTimers(); vi.restoreAllMocks() })
  it('explicitly checks one matching revision, marks existing targets and never writes board state', async () => {
    const { wrapper, load } = setup()
    expect(api.preview).not.toHaveBeenCalled()
    await load()
    expect(wrapper.emitted('markers')?.at(-1)?.[0]).toEqual({ 'card:a1': 'Proposed change', 'column:c1': 'Proposed change' })
    expect(wrapper.get('pre').text()).toContain('<script>unsafe</script>')
    expect(wrapper.find('script').exists()).toBe(false)
    expect(wrapper.props('cards')).toEqual([card])
    expect(wrapper.props('board')).toEqual(board)
    expect(wrapper.findAll('button').map(button => button.text())).toEqual(['Refresh board preview', 'Close preview'])
    expect(wrapper.getComponent({ name: 'RouterLink' }).props('to')).toEqual({ path: '/workspace/review', query: { boardId: 'b1' }, hash: '#proposal-p1' })
    wrapper.unmount()
  })
  it.each(['board', 'revision', 'updated', 'status'])('rejects mismatched %s receipts', async mismatch => {
    const value = detail()
    if (mismatch === 'board') value.boardId = 'other'
    if (mismatch === 'revision') value.latestRevisionId = 'r3'
    if (mismatch === 'updated') value.updatedAt = new Date(Date.parse(at) + 1000).toISOString()
    if (mismatch === 'status') value.status = 'Applied'
    api.detail.mockResolvedValue(value)
    const { wrapper, load } = setup(); await load()
    expect(wrapper.find('pre').exists()).toBe(false)
    expect(wrapper.emitted('markers')?.at(-1)?.[0]).toEqual({})
    wrapper.unmount()
  })
  it('clears displayed markers immediately on close before route removal', async () => {
    const { wrapper, load } = setup(); await load()
    await wrapper.findAll('button')[1]!.trigger('click')
    expect(wrapper.emitted('close')).toHaveLength(1)
    expect(wrapper.find('pre').exists()).toBe(false)
    expect(wrapper.emitted('markers')?.at(-1)?.[0]).toEqual({})
    wrapper.unmount()
  })
  it('ignores a pending response after close even while route removal is delayed', async () => {
    let resolve!: (value: ProposalPreview) => void
    api.preview.mockReturnValue(new Promise<ProposalPreview>(done => { resolve = done }))
    const { wrapper } = setup()
    await wrapper.get('button').trigger('click')
    await wrapper.findAll('button')[1]!.trigger('click')
    resolve(receipt()); await flushPromises()
    expect(wrapper.emitted('close')).toHaveLength(1)
    expect(wrapper.find('pre').exists()).toBe(false)
    expect(wrapper.emitted('markers')?.every(event => Object.keys(event[0] as BoardProposalMarkers).length === 0)).toBe(true)
    expect(wrapper.get('button').text()).toBe('Refresh board preview')
    wrapper.unmount()
  })
  it('uses the approved pin instead of a newer pending revision', async () => {
    api.preview.mockResolvedValue({ ...receipt(), status: 'Approved' })
    api.detail.mockResolvedValue({ ...detail(), status: 'Approved', latestRevisionId: 'r3', approvedRevisionId: 'r2' })
    const { wrapper, load } = setup(); await load()
    expect(wrapper.find('pre').exists()).toBe(true); wrapper.unmount()
  })
  it('retracts when board access or refresh fails and waits for board recovery', async () => {
    const { wrapper, load } = setup(); await load()
    await wrapper.setProps({ available: false })
    expect(wrapper.find('pre').exists()).toBe(false)
    expect(wrapper.emitted('markers')?.at(-1)?.[0]).toEqual({})
    expect(wrapper.get('button').attributes('disabled')).toBeDefined()
    await wrapper.setProps({ available: true }); await load()
    expect(wrapper.find('pre').exists()).toBe(true)
    wrapper.unmount()
  })
  it('retracts on board changes, permission failure, logout and stale in-flight responses', async () => {
    const { wrapper, load, session } = setup(); await load()
    await wrapper.setProps({ cards: [{ ...card, title: 'Externally changed' }] })
    expect(wrapper.find('pre').exists()).toBe(false)
    expect(wrapper.emitted('markers')?.at(-1)?.[0]).toEqual({})
    api.preview.mockRejectedValueOnce({ response: { status: 403 } }); await load()
    expect(wrapper.find('pre').exists()).toBe(false)
    let resolve!: (value: ProposalPreview) => void
    api.preview.mockReturnValue(new Promise<ProposalPreview>(done => { resolve = done }))
    await wrapper.get('button').trigger('click')
    session.token = null; resolve(receipt()); await flushPromises()
    expect(wrapper.find('pre').exists()).toBe(false)
    expect(wrapper.emitted('markers')?.at(-1)?.[0]).toEqual({})
    wrapper.unmount()
  })
  it('retains a same-user token refresh, expires on server time and clears markers on unmount', async () => {
    vi.useFakeTimers()
    const now = Date.now() + 3600000
    api.preview.mockResolvedValue({ ...receipt(), checkedAt: new Date(now).toISOString(), expiresAt: new Date(now + 5000).toISOString() })
    const { wrapper, load, session } = setup(); await load()
    session.token = 'refreshed'; await flushPromises()
    expect(wrapper.find('pre').exists()).toBe(true)
    await vi.advanceTimersByTimeAsync(5000)
    expect(wrapper.find('pre').exists()).toBe(false)
    expect(wrapper.emitted('markers')?.at(-1)?.[0]).toEqual({})
    wrapper.unmount()
  })
  it.each([CardItem, PaperBoardCard])('reactively labels saved cards in both renderers', async component => {
    const markers = ref<BoardProposalMarkers>({ 'card:a1': 'Proposed change' })
    const Host = defineComponent({ components: { Target: component }, setup() { provide(BOARD_PROPOSAL_MARKERS, computed(() => markers.value)); return { card } }, template: '<Target :card="card" />' })
    const wrapper = mount(Host)
    expect(wrapper.get('[data-card-id="a1"]').attributes('data-proposal-change')).toBe('true')
    expect(wrapper.text()).toContain('Saved title')
    expect(wrapper.text()).toContain('Proposed change')
    markers.value = {}; await flushPromises()
    expect(wrapper.find('[data-proposal-change]').exists()).toBe(false)
    wrapper.unmount()
  })
})
