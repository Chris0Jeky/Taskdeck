import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import CardRelations from '../../components/thinking/CardRelations.vue'
import { cardRelationsApi } from '../../api/cardRelationsApi'
import { cardsApi } from '../../api/cardsApi'
import { boardsApi } from '../../api/boardsApi'
import { useSessionStore } from '../../store/sessionStore'
import type { BoardDetail, Card } from '../../types/board'

vi.mock('../../api/cardRelationsApi', () => ({ cardRelationsApi: { get: vi.fn(), addProposal: vi.fn(), removeProposal: vi.fn() } }))
vi.mock('../../api/cardsApi', () => ({ cardsApi: { getCards: vi.fn(), getArchivedCards: vi.fn() } }))
vi.mock('../../api/boardsApi', () => ({ boardsApi: { getBoard: vi.fn() } }))

const graph = {
  boardId: 'board', revision: 7, canWrite: true,
  relations: [
    { sourceCardId: 'a', targetCardId: 'b', relationType: 'blocks' as const },
    { sourceCardId: 'c', targetCardId: 'a', relationType: 'spawned-from' as const },
  ],
}
const activeCards = [
  { id: 'a', title: 'Ship', columnId: 'next', isArchived: false },
  { id: 'b', title: 'Prepare', columnId: 'next', isArchived: false },
  { id: 'c', title: 'Origin', columnId: 'done', isArchived: false },
  { id: 'd', title: 'Explore', columnId: 'next', isArchived: false },
] as Card[]
const archivedCards = [{ id: 'archived', title: 'Retained', columnId: 'done', isArchived: true }] as Card[]

function create(canWrite = true) {
  return mount(CardRelations, {
    props: { boardId: 'board', cardId: 'a', canWrite },
    global: { stubs: { RouterLink: { props: ['to'], template: '<a :data-to="to"><slot /></a>' } } },
  })
}

async function open(wrapper: ReturnType<typeof create>) {
  await wrapper.get('button.toggle').trigger('click')
  await flushPromises()
}

describe('CardRelations', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.resetAllMocks()
    vi.mocked(cardRelationsApi.get).mockResolvedValue(structuredClone(graph))
    vi.mocked(cardsApi.getCards).mockResolvedValue(activeCards)
    vi.mocked(cardsApi.getArchivedCards).mockResolvedValue(archivedCards)
    vi.mocked(boardsApi.getBoard).mockResolvedValue({ isArchived: false, columns: [{ id: 'next', name: 'Next' }, { id: 'done', name: 'Done' }] } as BoardDetail)
  })

  it('loads only after expansion and renders both outgoing and incoming directions', async () => {
    const wrapper = create()
    expect(cardRelationsApi.get).not.toHaveBeenCalled()
    await open(wrapper)
    expect(wrapper.text()).toContain('This card blocks Prepare')
    expect(wrapper.text()).toContain('Origin was spawned from this card')
    expect(wrapper.findAll('a').map(link => link.text())).toContain('Origin')
  })

  it('creates a single proposal from the observed revision and never changes the displayed graph optimistically', async () => {
    vi.mocked(cardRelationsApi.addProposal).mockResolvedValue({ id: 'proposal-1' } as never)
    const wrapper = create()
    await open(wrapper)
    await wrapper.get('[aria-label="Relation type"]').setValue('depends-on')
    await wrapper.get('[aria-label="Other card"]').setValue('d')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(cardRelationsApi.addProposal).toHaveBeenCalledWith({
      boardId: 'board', cardId: 'a', relatedCardId: 'd', relationType: 'depends-on', expectedRevision: 7,
    })
    expect(wrapper.text()).toContain('Proposal created.')
    expect(wrapper.text()).toContain('This card blocks Prepare')
    expect(wrapper.find('[data-to="/workspace/review#proposal-proposal-1"]').exists()).toBe(true)
  })

  it('keeps a selected card on a stale revision and gives a refresh recovery', async () => {
    vi.mocked(cardRelationsApi.addProposal).mockRejectedValue({ response: { status: 409 } })
    const wrapper = create()
    await open(wrapper)
    await wrapper.get('[aria-label="Other card"]').setValue('d')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect((wrapper.get('[aria-label="Other card"]').element as HTMLSelectElement).value).toBe('d')
    expect(wrapper.get('[role="alert"]').text()).toContain('Relations changed')
    expect(wrapper.text()).toContain('Refresh relations to use the current version')
    expect(wrapper.get('form button').attributes('disabled')).toBeDefined()
  })

  it('uses the canonical source endpoint when proposing removal of an incoming relation', async () => {
    vi.mocked(cardRelationsApi.removeProposal).mockResolvedValue({ id: 'proposal-2' } as never)
    const wrapper = create()
    await open(wrapper)
    await wrapper.get('[aria-label="Propose removing Origin was spawned from this card"]').trigger('click')
    await flushPromises()
    expect(cardRelationsApi.removeProposal).toHaveBeenCalledWith({
      boardId: 'board', cardId: 'c', relatedCardId: 'a', relationType: 'spawned-from', expectedRevision: 7,
    })
  })

  it('keeps viewers and archived cards readable but proposal controls absent', async () => {
    const viewer = create(false)
    await open(viewer)
    expect(viewer.text()).toContain('This card blocks Prepare')
    expect(viewer.find('form').exists()).toBe(false)
    expect(viewer.find('[aria-label^="Propose removing"]').exists()).toBe(false)

    vi.mocked(cardsApi.getCards).mockResolvedValue([{ ...activeCards[0], isArchived: true }, ...activeCards.slice(1)] as Card[])
    const archived = create(true)
    await open(archived)
    expect(archived.text()).toContain('This card is archived')
    expect(archived.find('form').exists()).toBe(false)
  })

  it('does not offer a write when the loaded endpoint does not belong to this board', async () => {
    vi.mocked(cardsApi.getCards).mockResolvedValue(activeCards.filter(card => card.id !== 'a'))
    const wrapper = create()
    await open(wrapper)
    expect(wrapper.text()).toContain('no longer available on this board')
    expect(wrapper.find('form').exists()).toBe(false)
    expect(cardRelationsApi.addProposal).not.toHaveBeenCalled()
  })

  it('ignores a graph response from the previous account', async () => {
    let resolve!: (value: typeof graph) => void
    vi.mocked(cardRelationsApi.get).mockReturnValue(new Promise(resolvePromise => { resolve = resolvePromise }))
    const wrapper = create()
    await wrapper.get('button.toggle').trigger('click')
    useSessionStore().userId = 'another-user'
    resolve(graph)
    await flushPromises()
    expect(wrapper.text()).not.toContain('This card blocks Prepare')
    expect(wrapper.get('button.toggle').attributes('aria-expanded')).toBe('false')
  })
})
