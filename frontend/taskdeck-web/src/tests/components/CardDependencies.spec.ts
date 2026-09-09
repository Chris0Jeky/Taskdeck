import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import CardDependencies from '../../components/thinking/CardDependencies.vue'
import { boardDependenciesApi } from '../../api/boardDependenciesApi'
import { cardsApi } from '../../api/cardsApi'
import { boardsApi } from '../../api/boardsApi'
import { useSessionStore } from '../../store/sessionStore'
import type { BoardDetail, Card } from '../../types/board'

vi.mock('../../api/boardDependenciesApi', () => ({ boardDependenciesApi: { get: vi.fn(), save: vi.fn() } }))
vi.mock('../../api/cardsApi', () => ({ cardsApi: { getCards: vi.fn() } }))
vi.mock('../../api/boardsApi', () => ({ boardsApi: { getBoard: vi.fn() } }))
const graph = { boardId: 'board', revision: 2, canWrite: true, edges: [{ cardId: 'a', dependsOnCardId: 'b' }] }
const cards = [{ id: 'a', title: 'Ship', columnId: 'next' }, { id: 'b', title: 'Prepare', columnId: 'next', isBlocked: true, blockReason: 'Waiting for source' }, { id: 'c', title: 'Check', columnId: 'next' }] as Card[]
function create() { return mount(CardDependencies, { props: { boardId: 'board', cardId: 'a' }, global: { stubs: { RouterLink: { template: '<a><slot /></a>' } } } }) }
async function open(wrapper: ReturnType<typeof create>) { await wrapper.get('button').trigger('click'); await flushPromises() }
describe('CardDependencies', () => {
  beforeEach(() => {
    setActivePinia(createPinia()); vi.resetAllMocks()
    vi.mocked(boardDependenciesApi.get).mockResolvedValue(structuredClone(graph))
    vi.mocked(cardsApi.getCards).mockResolvedValue(cards)
    vi.mocked(boardsApi.getBoard).mockResolvedValue({ columns: [{ id: 'next', name: 'Next' }] } as BoardDetail)
  })
  it('loads on demand and adds only an explicit selected prerequisite using the saved revision', async () => {
    const wrapper = create()
    expect(boardDependenciesApi.get).not.toHaveBeenCalled()
    await open(wrapper)
    expect(wrapper.text()).toContain('Waiting for source')
    expect(boardDependenciesApi.save).not.toHaveBeenCalled()
    const edges = [...graph.edges, { cardId: 'a', dependsOnCardId: 'c' }]
    vi.mocked(boardDependenciesApi.save).mockResolvedValue({ ...graph, revision: 3, edges })
    await wrapper.get('select').setValue('c'); await wrapper.get('form').trigger('submit'); await flushPromises()
    expect(boardDependenciesApi.save).toHaveBeenCalledWith('board', 2, edges)
    expect(wrapper.text()).toContain('Check')
    expect(wrapper.emitted('busy')).toEqual([[true], [false]])
  })
  it('requires a fresh read after conflicts and removes stale metadata', async () => {
    const wrapper = create(); await open(wrapper)
    vi.mocked(boardDependenciesApi.save).mockRejectedValue({ response: { status: 409 } })
    await wrapper.get('[aria-label="Remove prerequisite Prepare"]').trigger('click'); await flushPromises()
    expect(wrapper.find('form').exists()).toBe(false)
    expect(wrapper.text()).not.toContain('Waiting for source')
    expect(wrapper.find('[role="alert"]').exists()).toBe(true)
  })
  it('ignores old load results after account change', async () => {
    let resolve!: (value: typeof graph) => void
    vi.mocked(boardDependenciesApi.get).mockReturnValue(new Promise(r => { resolve = r }))
    const wrapper = create(); await open(wrapper)
    useSessionStore().userId = 'different-user'
    resolve(graph); await flushPromises()
    expect(wrapper.text()).not.toContain('Prepare')
    expect(wrapper.get('button').attributes('aria-expanded')).toBe('false')
  })
  it('shows incoming relationships and no write controls for viewers', async () => {
    vi.mocked(boardDependenciesApi.get).mockResolvedValue({ ...graph, canWrite: false, edges: [{ cardId: 'b', dependsOnCardId: 'a' }] })
    const wrapper = create(); await open(wrapper)
    expect(wrapper.text()).toContain('No prerequisites chosen')
    expect(wrapper.text()).toContain('Prepare')
    expect(wrapper.find('form').exists()).toBe(false)
    expect(wrapper.find('[aria-label^="Remove prerequisite"]').exists()).toBe(false)
  })
})
