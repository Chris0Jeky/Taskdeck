import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import BoardCardArchive from '../../components/board/BoardCardArchive.vue'
import CardArchiveAction from '../../components/board/CardArchiveAction.vue'
import { useBoardStore } from '../../store/boardStore'
import { cardsApi } from '../../api/cardsApi'
import type { Card, BoardDetail } from '../../types/board'

vi.mock('../../api/cardsApi', () => ({ cardsApi: { getArchivedCards: vi.fn(), setArchived: vi.fn() } }))
const card = { id: 'c', boardId: 'b', columnId: 'col', title: 'Retained', description: 'Evidence',
  labels: [], updatedAt: '2026-09-10T10:00:00Z', isArchived: true } as unknown as Card
const mountHistory = () => mount(BoardCardArchive, { props: { boardId: 'b' }, global: { stubs: { RouterLink: true } } })
describe('Card archive', () => {
  beforeEach(() => {
    setActivePinia(createPinia()); vi.clearAllMocks()
    useBoardStore().currentBoard = { id: 'b', canWrite: true, isArchived: false, columns: [] } as unknown as BoardDetail
    vi.mocked(cardsApi.getArchivedCards).mockResolvedValue([card])
  })
  it('loads on explicit request and restores the displayed revision', async () => {
    const wrapper = mountHistory()
    expect(cardsApi.getArchivedCards).not.toHaveBeenCalled()
    await wrapper.get('button').trigger('click'); await flushPromises()
    expect(wrapper.text()).toContain('Retained')
    vi.mocked(cardsApi.setArchived).mockResolvedValue({ ...card, isArchived: false })
    vi.mocked(cardsApi.getArchivedCards).mockResolvedValue([])
    await wrapper.findComponent(CardArchiveAction).get('button').trigger('click'); await flushPromises()
    expect(cardsApi.setArchived).toHaveBeenCalledWith('b', 'c', false, card.updatedAt)
    expect(wrapper.text()).toContain('No archived cards')
    expect(useBoardStore().currentBoardCards[0]?.id).toBe('c')
  })
  it('shows read errors and allows a fresh read', async () => {
    vi.mocked(cardsApi.getArchivedCards).mockRejectedValueOnce(new Error('Offline'))
    const wrapper = mountHistory()
    await wrapper.get('button').trigger('click'); await flushPromises()
    expect(wrapper.find('[role="alert"]').exists()).toBe(true)
    await wrapper.findAll('button')[1]!.trigger('click'); await flushPromises()
    expect(wrapper.text()).toContain('Retained')
  })
  it('requires refresh after a stale or uncertain write and never retries it automatically', async () => {
    vi.mocked(cardsApi.setArchived).mockRejectedValue(new Error('Card changed'))
    const wrapper = mount(CardArchiveAction, { props: { card } })
    await wrapper.get('button').trigger('click'); await flushPromises()
    expect(wrapper.get('button').attributes('disabled')).toBeDefined()
    expect(wrapper.text()).toContain('Refresh card state')
    expect(cardsApi.setArchived).toHaveBeenCalledTimes(1)
  })
  it('disables restore for viewers and archived boards', () => {
    useBoardStore().currentBoard!.canWrite = false
    const viewer = mount(CardArchiveAction, { props: { card } })
    expect(viewer.get('button').attributes('disabled')).toBeDefined()
    useBoardStore().currentBoard!.canWrite = true
    useBoardStore().currentBoard!.isArchived = true
    const archived = mount(CardArchiveAction, { props: { card } })
    expect(archived.get('button').attributes('disabled')).toBeDefined()
  })
})
