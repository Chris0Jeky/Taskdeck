import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import CardRelations from '../../components/thinking/CardRelations.vue'
import { cardRelationsApi } from '../../api/cardRelationsApi'
import { cardsApi } from '../../api/cardsApi'
import { boardsApi } from '../../api/boardsApi'
import type { BoardDetail, Card } from '../../types/board'
import type { BoardCardRelations } from '../../types/cardRelations'

vi.mock('../../api/cardRelationsApi', () => ({ cardRelationsApi: { get: vi.fn(), addProposal: vi.fn(), removeProposal: vi.fn() } }))
vi.mock('../../api/cardsApi', () => ({ cardsApi: { getCards: vi.fn(), getArchivedCards: vi.fn() } }))
vi.mock('../../api/boardsApi', () => ({ boardsApi: { getBoard: vi.fn() } }))

const graph: BoardCardRelations = {
  boardId: 'board', revision: 7, canWrite: true,
  relations: [{ sourceCardId: 'a', targetCardId: 'b', relationType: 'blocks' }],
}
const cards = [
  { id: 'a', title: 'Ship', columnId: 'next', isArchived: false },
  { id: 'b', title: 'Prepare', columnId: 'next', isArchived: false },
  { id: 'c', title: 'Explore', columnId: 'next', isArchived: false },
] as Card[]
function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: Error) => void
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}
const wrappers: ReturnType<typeof mount>[] = []
async function openPanel(canWrite = true) {
  const wrapper = mount(CardRelations, {
    props: { boardId: 'board', cardId: 'a', canWrite },
    global: { stubs: { RouterLink: { props: ['to'], template: '<a :data-to="to"><slot /></a>' } } },
  })
  wrappers.push(wrapper)
  await wrapper.get('button.toggle').trigger('click')
  await flushPromises()
  return wrapper
}
const removal = '[aria-label="Propose removing This card blocks Prepare"]'

describe('CardRelations refresh admission (#3070)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.resetAllMocks()
    vi.mocked(cardRelationsApi.get).mockResolvedValue(structuredClone(graph))
    vi.mocked(cardsApi.getCards).mockResolvedValue(cards)
    vi.mocked(cardsApi.getArchivedCards).mockResolvedValue([])
    vi.mocked(boardsApi.getBoard).mockResolvedValue({ isArchived: false, columns: [{ id: 'next', name: 'Next' }] } as BoardDetail)
    vi.mocked(cardRelationsApi.addProposal).mockResolvedValue({ id: 'added' } as never)
    vi.mocked(cardRelationsApi.removeProposal).mockResolvedValue({ id: 'removed' } as never)
  })
  afterEach(() => { for (const wrapper of wrappers.splice(0)) wrapper.unmount() })

  it('retains graph and choices but disables all mutation affordances until the new revision arrives', async () => {
    const wrapper = await openPanel()
    await wrapper.get('[aria-label="Other card"]').setValue('c')
    await wrapper.get('[aria-label="Relation type"]').setValue('depends-on')
    const originalRemove = wrapper.get(removal).element
    const pending = deferred<BoardCardRelations>()
    vi.mocked(cardRelationsApi.get).mockReturnValueOnce(pending.promise)
    await wrapper.get('header button').trigger('click')

    expect(wrapper.text()).toContain('This card blocks Prepare')
    expect(wrapper.text()).toContain('Loading card relations')
    expect(wrapper.get(removal).element).toBe(originalRemove)
    for (const selector of [removal, 'form button', '[aria-label="Other card"]', '[aria-label="Relation type"]']) {
      expect(wrapper.get(selector).attributes('disabled')).toBeDefined()
    }
    await wrapper.get(removal).trigger('click')
    // A synthetic submission also checks the existing handler's defense in depth.
    await wrapper.get('form').trigger('submit')
    expect(cardRelationsApi.addProposal).not.toHaveBeenCalled()
    expect(cardRelationsApi.removeProposal).not.toHaveBeenCalled()
    expect(wrapper.emitted('busy')).toBeUndefined()

    pending.resolve({ ...graph, revision: 8 })
    await flushPromises()
    expect(wrapper.get(removal).attributes('disabled')).toBeUndefined()
    expect(wrapper.get('form button').attributes('disabled')).toBeUndefined()
    expect((wrapper.get('[aria-label="Other card"]').element as HTMLSelectElement).value).toBe('c')
    expect((wrapper.get('[aria-label="Relation type"]').element as HTMLSelectElement).value).toBe('depends-on')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(cardRelationsApi.addProposal).toHaveBeenCalledExactlyOnceWith({
      boardId: 'board', cardId: 'a', relatedCardId: 'c', relationType: 'depends-on', expectedRevision: 8,
    })
    expect(wrapper.text()).toContain('This card blocks Prepare')
  })

  it('reenables removal with the refreshed revision, without applying it locally', async () => {
    const wrapper = await openPanel()
    const pending = deferred<BoardCardRelations>()
    vi.mocked(cardRelationsApi.get).mockReturnValueOnce(pending.promise)
    await wrapper.get('header button').trigger('click')
    expect(wrapper.get(removal).attributes('disabled')).toBeDefined()
    pending.resolve({ ...graph, revision: 12 })
    await flushPromises()
    await wrapper.get(removal).trigger('click')
    await flushPromises()
    expect(cardRelationsApi.removeProposal).toHaveBeenCalledExactlyOnceWith({
      boardId: 'board', cardId: 'a', relatedCardId: 'b', relationType: 'blocks', expectedRevision: 12,
    })
    expect(wrapper.text()).toContain('This card blocks Prepare')
  })

  it('fails closed after refresh rejection and recovers retained choices on a successful retry', async () => {
    const wrapper = await openPanel()
    await wrapper.get('[aria-label="Other card"]').setValue('c')
    await wrapper.get('[aria-label="Relation type"]').setValue('duplicates')
    const pending = deferred<BoardCardRelations>()
    vi.mocked(cardRelationsApi.get).mockReturnValueOnce(pending.promise)
    await wrapper.get('header button').trigger('click')
    expect(wrapper.get('form button').attributes('disabled')).toBeDefined()
    pending.reject(new Error('Offline'))
    await flushPromises()
    expect(wrapper.find('[role="alert"]').exists()).toBe(true)
    expect(wrapper.find('form').exists()).toBe(false)
    expect(wrapper.find(removal).exists()).toBe(false)
    expect(wrapper.get('header button').attributes('disabled')).toBeUndefined()
    await wrapper.get('header button').trigger('click')
    await flushPromises()
    expect(wrapper.find('[role="alert"]').exists()).toBe(false)
    expect((wrapper.get('[aria-label="Other card"]').element as HTMLSelectElement).value).toBe('c')
    expect((wrapper.get('[aria-label="Relation type"]').element as HTMLSelectElement).value).toBe('duplicates')
    expect(wrapper.get('form button').attributes('disabled')).toBeUndefined()
    expect(cardRelationsApi.addProposal).not.toHaveBeenCalled()
  })

  it.each(['permission', 'board-archive', 'card-archive'] as const)('does not restore mutation controls after refreshed %s denial', async (denial) => {
    const wrapper = await openPanel()
    await wrapper.get('[aria-label="Other card"]').setValue('c')
    const pending = deferred<BoardCardRelations>()
    vi.mocked(cardRelationsApi.get).mockReturnValueOnce(pending.promise)
    if (denial === 'board-archive') vi.mocked(boardsApi.getBoard).mockResolvedValueOnce({ isArchived: true, columns: [] } as unknown as BoardDetail)
    if (denial === 'card-archive') vi.mocked(cardsApi.getCards).mockResolvedValueOnce(cards.map(card => card.id === 'a' ? { ...card, isArchived: true } : card))
    await wrapper.get('header button').trigger('click')
    expect(wrapper.get(removal).attributes('disabled')).toBeDefined()
    pending.resolve({ ...graph, revision: 8, canWrite: denial !== 'permission' })
    await flushPromises()
    expect(wrapper.find('form').exists()).toBe(false)
    expect(wrapper.find(removal).exists()).toBe(false)
    expect(wrapper.text()).toContain('This card blocks Prepare')
    expect(cardRelationsApi.addProposal).not.toHaveBeenCalled()
    expect(cardRelationsApi.removeProposal).not.toHaveBeenCalled()
  })

  it('keeps a viewer read-only throughout a refresh', async () => {
    const wrapper = await openPanel(false)
    const pending = deferred<BoardCardRelations>()
    vi.mocked(cardRelationsApi.get).mockReturnValueOnce(pending.promise)
    await wrapper.get('header button').trigger('click')
    expect(wrapper.find('form').exists()).toBe(false)
    expect(wrapper.find(removal).exists()).toBe(false)
    pending.resolve({ ...graph, revision: 9 })
    await flushPromises()
    expect(wrapper.find('form').exists()).toBe(false)
    expect(wrapper.find(removal).exists()).toBe(false)
  })

  it('clears an invalidated endpoint selection instead of proposing from a vanished choice', async () => {
    const wrapper = await openPanel()
    await wrapper.get('[aria-label="Other card"]').setValue('c')
    const pending = deferred<BoardCardRelations>()
    vi.mocked(cardRelationsApi.get).mockReturnValueOnce(pending.promise)
    vi.mocked(cardsApi.getCards).mockResolvedValueOnce(cards.filter(card => card.id !== 'c'))
    await wrapper.get('header button').trigger('click')
    pending.resolve({ ...graph, revision: 9 })
    await flushPromises()
    expect((wrapper.get('[aria-label="Other card"]').element as HTMLSelectElement).value).toBe('')
    expect(wrapper.get('form button').attributes('disabled')).toBeDefined()
    expect(cardRelationsApi.addProposal).not.toHaveBeenCalled()
  })
})
