import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import ThinkingStepCard from '../../components/thinking/ThinkingStepCard.vue'
import { boardsApi } from '../../api/boardsApi'
import { cardsApi } from '../../api/cardsApi'
import { thinkingApi } from '../../api/thinkingApi'
import type { BoardDetail, Card } from '../../types/board'

vi.mock('../../api/boardsApi', () => ({ boardsApi: { getBoard: vi.fn() } }))
vi.mock('../../api/cardsApi', () => ({ cardsApi: { getCards: vi.fn() } }))
vi.mock('../../api/thinkingApi', () => ({ thinkingApi: { promote: vi.fn() } }))
const props = { boardId: 'board', cardId: 'parent', layerId: 'layer', item: { id: 'step', text: 'A useful step', completed: false }, revision: 3, sourceReady: true, canWrite: true }
const board = { id: 'board', isArchived: false, columns: [{ id: 'todo', name: 'To do' }, { id: 'done', name: 'Done' }] } as BoardDetail
beforeEach(() => {
  vi.clearAllMocks()
  vi.mocked(boardsApi.getBoard).mockResolvedValue(board)
  vi.mocked(cardsApi.getCards).mockResolvedValue([])
})
const mountStep = (overrides = {}) => mount(ThinkingStepCard, { props: { ...props, ...overrides }, global: { stubs: { RouterLink: { template: '<a><slot /></a>' } } } })
describe('ThinkingStepCard', () => {
  it('requires saved source and an explicit destination before submitting the revision', async () => {
    const wrapper = mountStep({ sourceReady: false })
    expect(wrapper.get('button').attributes('disabled')).toBeDefined()
    expect(boardsApi.getBoard).not.toHaveBeenCalled()
    await wrapper.setProps({ sourceReady: true })
    await wrapper.get('button').trigger('click')
    await flushPromises()
    const create = () => wrapper.findAll('button').find(button => button.text() === 'Create linked card')!
    expect(create().attributes('disabled')).toBeDefined()
    await wrapper.get('select').setValue('todo')
    vi.mocked(thinkingApi.promote).mockResolvedValue({ cardId: 'parent', revision: 4, schemaVersion: 1, canWrite: true, layers: [] })
    await create().trigger('click')
    await flushPromises()
    expect(thinkingApi.promote).toHaveBeenCalledWith('board', 'parent', 'layer', 'step', 3, 'todo', 'A useful step')
    expect(wrapper.emitted('promoted')).toHaveLength(1)
    expect(wrapper.emitted('busy')).toEqual([[true], [false]])
    expect(wrapper.emitted('dirty-change')).toEqual([[true], [false]])
  })
  it('retains choices on refusal and does not replace the deck', async () => {
    const wrapper = mountStep()
    await wrapper.get('button').trigger('click'); await flushPromises()
    await wrapper.get('input').setValue('Keep my title')
    await wrapper.get('select').setValue('todo')
    vi.mocked(thinkingApi.promote).mockRejectedValue(new Error('offline'))
    await wrapper.findAll('button').find(button => button.text() === 'Create linked card')!.trigger('click')
    await flushPromises()
    expect(wrapper.find('[role="alert"]').exists()).toBe(true)
    expect((wrapper.get('input').element as HTMLInputElement).value).toBe('Keep my title')
    expect((wrapper.get('select').element as HTMLSelectElement).value).toBe('todo')
    expect(wrapper.emitted('promoted')).toBeUndefined()
  })
  it('reads real column and blocker status for viewers, refreshes, and never offers duplicate creation', async () => {
    vi.mocked(cardsApi.getCards).mockResolvedValue([{ id: 'child', title: 'Real child', columnId: 'todo', isBlocked: true } as Card])
    const wrapper = mountStep({ item: { ...props.item, linkedCardId: 'child' }, canWrite: false })
    await flushPromises()
    expect(wrapper.text()).toContain('To do · Blocked')
    expect(wrapper.text()).not.toContain('Create card from step')
    vi.mocked(cardsApi.getCards).mockResolvedValue([{ id: 'child', title: 'Renamed child', columnId: 'done', isBlocked: false } as Card])
    await wrapper.get('button').trigger('click'); await flushPromises()
    expect(wrapper.text()).toContain('Renamed child')
    expect(wrapper.text()).toContain('Done')
    expect(wrapper.text()).not.toContain('Blocked')
    vi.mocked(cardsApi.getCards).mockResolvedValue([])
    await wrapper.get('button').trigger('click'); await flushPromises()
    expect(wrapper.text()).toContain('no longer available')
    expect(thinkingApi.promote).not.toHaveBeenCalled()
  })
})
