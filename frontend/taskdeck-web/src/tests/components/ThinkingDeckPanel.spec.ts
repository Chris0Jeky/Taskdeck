import { mount, flushPromises } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import ThinkingDeckPanel from '../../components/thinking/ThinkingDeckPanel.vue'
import CardDependencies from '../../components/thinking/CardDependencies.vue'
import CardRelations from '../../components/thinking/CardRelations.vue'
import { thinkingApi } from '../../api/thinkingApi'
import { boardDependenciesApi } from '../../api/boardDependenciesApi'
import { cardsApi } from '../../api/cardsApi'
import { boardsApi } from '../../api/boardsApi'
import type { BoardDetail, Card } from '../../types/board'

vi.mock('../../api/thinkingApi', () => ({ thinkingApi: { get: vi.fn(), save: vi.fn() } }))
vi.mock('../../api/boardDependenciesApi', () => ({ boardDependenciesApi: { get: vi.fn(), save: vi.fn() } }))
vi.mock('../../api/cardsApi', () => ({ cardsApi: { getCards: vi.fn() } }))
vi.mock('../../api/boardsApi', () => ({ boardsApi: { getBoard: vi.fn() } }))
const props = { boardId: 'board-a', cardId: 'card-a' }
const graph = { boardId: 'board-a', revision: 2, canWrite: true, edges: [{ cardId: 'card-a', dependsOnCardId: 'prerequisite' }] }
const cards = [{ id: 'card-a', title: 'Deliver' }, { id: 'prerequisite', title: 'Prepare' }] as Card[]
function deck(canWrite: boolean, cardId = 'card-a') { return { cardId, revision: 0, schemaVersion: 1, canWrite, layers: [] } }
beforeEach(() => {
  vi.clearAllMocks()
  vi.mocked(thinkingApi.get).mockResolvedValue(deck(true))
  vi.mocked(boardDependenciesApi.get).mockResolvedValue(structuredClone(graph))
  vi.mocked(cardsApi.getCards).mockResolvedValue(cards)
  vi.mocked(boardsApi.getBoard).mockResolvedValue({ columns: [] } as unknown as BoardDetail)
})

describe('ThinkingDeckPanel', () => {
  it('passes only current confirmed card write permission to relation panels across card reads', async () => {
    let resolve!: (value: Awaited<ReturnType<typeof thinkingApi.get>>) => void
    vi.mocked(thinkingApi.get).mockResolvedValueOnce({ cardId: 'card-a', revision: 0, schemaVersion: 1, canWrite: false, layers: [] })
    const wrapper = mount(ThinkingDeckPanel, { props, global: { stubs: { CardDependencies: true, CardRelations: true } } })
    await flushPromises()
    expect(wrapper.getComponent(CardDependencies).props('canWrite')).toBe(false)
    expect(wrapper.getComponent(CardRelations).props('canWrite')).toBe(false)
    vi.mocked(thinkingApi.get).mockReturnValueOnce(new Promise(r => { resolve = r }))
    await wrapper.setProps({ cardId: 'restored-card' })
    expect(wrapper.findComponent(CardDependencies).exists()).toBe(false)
    resolve({ cardId: 'restored-card', revision: 0, schemaVersion: 1, canWrite: true, layers: [] })
    await flushPromises()
    expect(wrapper.getComponent(CardDependencies).props('canWrite')).toBe(true)
    expect(wrapper.getComponent(CardRelations).props('canWrite')).toBe(true)
    vi.mocked(thinkingApi.get).mockRejectedValueOnce(new Error('unavailable'))
    await wrapper.setProps({ cardId: 'unavailable-card' }); await flushPromises()
    expect(wrapper.findComponent(CardDependencies).exists()).toBe(false)
    expect(wrapper.get('[role="alert"]').text()).toContain('Could not load')
    wrapper.unmount()
  })

  it('retains private drafts if removal was opened before the draft began', async () => {
    const wrapper = mount(ThinkingDeckPanel, { props, global: { stubs: { ThinkingQuestionAnswer: true, CardDependencies: true } } })
    await flushPromises()
    await wrapper.findAll('button').find(button => button.text() === '+ question')!.trigger('click')
    await wrapper.get('[aria-label="Remove layer 1"]').trigger('click')
    const answer = wrapper.getComponent({ name: 'ThinkingQuestionAnswer' })
    answer.vm.$emit('dirty-change', true); await flushPromises()
    expect(wrapper.get('[aria-label="Remove layer 1"]').attributes('disabled')).toBeDefined()
    const confirm = wrapper.findAll('button').find(button => button.text() === 'Remove layer')!
    expect(confirm.attributes('disabled')).toBeDefined()
    await confirm.trigger('click')
    expect(wrapper.findComponent({ name: 'ThinkingQuestionAnswer' }).exists()).toBe(true)
    expect(wrapper.text()).toContain('Keep or explicitly discard your private answer and audio draft')
    answer.vm.$emit('dirty-change', false); await flushPromises()
    await confirm.trigger('click')
    expect(wrapper.findComponent({ name: 'ThinkingQuestionAnswer' }).exists()).toBe(false)
    wrapper.unmount()
  })
  it('shows viewer read-only state while retaining presentation controls', async () => {
    vi.mocked(thinkingApi.get).mockResolvedValue({ cardId: 'card-a', revision: 0, schemaVersion: 1, canWrite: false, layers: [] })
    const wrapper = mount(ThinkingDeckPanel, { props })
    await flushPromises()
    expect(wrapper.text()).toContain('Read-only')
    expect(wrapper.find('fieldset').attributes('disabled')).toBeDefined()
    expect(wrapper.findAll('button').some(button => button.text() === 'Save thinking')).toBe(false)
    await wrapper.findAll('button').find(button => button.text() === 'Path')!.trigger('click')
    expect(wrapper.find('.layers--path').exists()).toBe(true)
  })

  it('preserves draft and alternatives across stack/path changes and saves the real card ID', async () => {
    const wrapper = mount(ThinkingDeckPanel, { props })
    await flushPromises()
    await wrapper.findAll('button').find(button => button.text() === '+ options')!.trigger('click')
    await wrapper.find('[aria-label="Layer 1 title"]').setValue('Pick a route')
    await wrapper.findAll('button').find(button => button.text() === '+ Add option')!.trigger('click')
    await wrapper.findAll('button').find(button => button.text() === '+ Add option')!.trigger('click')
    await wrapper.find('[aria-label="options item 1"]').setValue('First route')
    await wrapper.find('[aria-label="options item 2"]').setValue('Second route')
    await wrapper.find('[aria-label="Choose option 2"]').setValue()
    await wrapper.findAll('button').find(button => button.text() === 'Path')!.trigger('click')
    expect(wrapper.findAll('input[type=radio]')).toHaveLength(2)
    expect((wrapper.find('[aria-label="Layer 1 title"]').element as HTMLInputElement).value).toBe('Pick a route')
    vi.mocked(thinkingApi.save).mockImplementation(async (_, cardId, revision, layers) => ({ cardId, revision: revision + 1, schemaVersion: 1, canWrite: true, layers }))
    await wrapper.findAll('button').find(button => button.text() === 'Save thinking')!.trigger('click')
    await flushPromises()
    expect(thinkingApi.save).toHaveBeenCalledWith('board-a', 'card-a', 0, expect.arrayContaining([expect.objectContaining({ title: 'Pick a route', items: expect.arrayContaining([expect.objectContaining({ text: 'First route' }), expect.objectContaining({ text: 'Second route' })]) })]))
    expect(wrapper.text()).toContain('Thinking saved')
    expect(wrapper.emitted('dirty-change')).toContainEqual([true])
  })

  it('keeps conflicting edits until explicit discard confirmation', async () => {
    const wrapper = mount(ThinkingDeckPanel, { props })
    await flushPromises()
    await wrapper.findAll('button').find(button => button.text() === '+ note')!.trigger('click')
    await wrapper.find('[aria-label="Layer 1 details"]').setValue('Keep my draft')
    vi.mocked(thinkingApi.save).mockRejectedValue({ response: { status: 409 } })
    await wrapper.findAll('button').find(button => button.text() === 'Save thinking')!.trigger('click')
    await flushPromises()
    expect(wrapper.text()).toContain('Someone saved a newer version')
    expect((wrapper.find('textarea').element as HTMLTextAreaElement).value).toBe('Keep my draft')
    await wrapper.findAll('button').find(button => button.text() === 'Load saved version…')!.trigger('click')
    expect(thinkingApi.get).toHaveBeenCalledTimes(1)
    await wrapper.findAll('button').find(button => button.text() === 'Keep draft')!.trigger('click')
    expect((wrapper.find('textarea').element as HTMLTextAreaElement).value).toBe('Keep my draft')
  })
})

// #2958: Refresh dependencies revalidates the card's server-authoritative permission in place, so a
// restore or an access grant made in another session no longer needs a page or route reload.
describe('ThinkingDeckPanel dependency refresh permission', () => {
  function open() {
    return mount(ThinkingDeckPanel, { props, global: { stubs: { RouterLink: { template: '<a><slot /></a>' } } } })
  }
  type Panel = ReturnType<typeof open>
  async function expand(wrapper: Panel) {
    await wrapper.findAll('button').find(button => button.text() === 'Explore dependencies')!.trigger('click')
    await flushPromises()
  }
  async function refresh(wrapper: Panel) {
    await wrapper.findAll('button').find(button => button.text() === 'Refresh dependencies')!.trigger('click')
    await flushPromises()
  }
  const writable = (wrapper: Panel) => wrapper.findAll('button').some(button => button.text() === 'Save thinking')

  it('makes controls writable when refresh sees the card restored', async () => {
    vi.mocked(thinkingApi.get).mockResolvedValue(deck(false))
    const wrapper = open(); await flushPromises()
    await expand(wrapper)
    expect(wrapper.text()).toContain('Editing needs an active card and board write access')
    expect(wrapper.find('form').exists()).toBe(false)
    expect(writable(wrapper)).toBe(false)
    vi.mocked(thinkingApi.get).mockResolvedValue(deck(true))
    await refresh(wrapper)
    expect(wrapper.find('form').exists()).toBe(true)
    expect(wrapper.find('[aria-label="Remove prerequisite Prepare"]').exists()).toBe(true)
    expect(writable(wrapper)).toBe(true)
    wrapper.unmount()
  })

  it('makes controls writable when refresh sees board access granted to a viewer', async () => {
    vi.mocked(thinkingApi.get).mockResolvedValue(deck(false))
    vi.mocked(boardDependenciesApi.get).mockResolvedValue({ ...structuredClone(graph), canWrite: false })
    const wrapper = open(); await flushPromises()
    await expand(wrapper)
    expect(wrapper.text()).toContain('Read-only')
    expect(wrapper.find('form').exists()).toBe(false)
    vi.mocked(thinkingApi.get).mockResolvedValue(deck(true))
    vi.mocked(boardDependenciesApi.get).mockResolvedValue(structuredClone(graph))
    await refresh(wrapper)
    expect(wrapper.find('form').exists()).toBe(true)
    expect(writable(wrapper)).toBe(true)
    wrapper.unmount()
  })

  it('keeps a still-denied card read-only after refresh', async () => {
    vi.mocked(thinkingApi.get).mockResolvedValue(deck(false))
    const wrapper = open(); await flushPromises()
    await expand(wrapper)
    await refresh(wrapper)
    expect(thinkingApi.get).toHaveBeenCalledTimes(3)
    expect(wrapper.text()).toContain('Editing needs an active card and board write access')
    expect(wrapper.find('form').exists()).toBe(false)
    expect(wrapper.find('[aria-label^="Remove prerequisite"]').exists()).toBe(false)
    expect(writable(wrapper)).toBe(false)
    expect(boardDependenciesApi.save).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('keeps controls read-only and shows the failure when the permission refresh fails', async () => {
    vi.mocked(thinkingApi.get).mockResolvedValue(deck(false))
    const wrapper = open(); await flushPromises()
    await expand(wrapper)
    vi.mocked(thinkingApi.get).mockRejectedValue({ response: { status: 500 } })
    await refresh(wrapper)
    expect(wrapper.get('[role="alert"]').text()).toContain('Could not load dependencies')
    expect(wrapper.find('form').exists()).toBe(false)
    expect(wrapper.text()).not.toContain('Prepare')
    expect(writable(wrapper)).toBe(false)
    wrapper.unmount()
  })

  it('withdraws write controls when the refresh reports access denied', async () => {
    const wrapper = open(); await flushPromises()
    await expand(wrapper)
    expect(wrapper.find('form').exists()).toBe(true)
    expect(writable(wrapper)).toBe(true)
    vi.mocked(thinkingApi.get).mockRejectedValue({ response: { status: 403, data: { errorCode: 'Forbidden' } } })
    await refresh(wrapper)
    expect(wrapper.get('[role="alert"]').text()).toContain('You do not have permission')
    expect(wrapper.find('form').exists()).toBe(false)
    expect(writable(wrapper)).toBe(false)
    expect(boardDependenciesApi.save).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('ignores a superseded permission read when two refreshes overlap', async () => {
    const wrapper = open(); await flushPromises()
    await expand(wrapper)
    expect(writable(wrapper)).toBe(true)
    let superseded!: (value: ReturnType<typeof deck>) => void
    vi.mocked(thinkingApi.get).mockReturnValueOnce(new Promise(r => { superseded = r }))
    await wrapper.findAll('button').find(button => button.text() === 'Refresh dependencies')!.trigger('click')
    // The panel can be hidden and reopened while that read is still in flight, which starts a newer
    // read; here the newer one sees the card archived again.
    await wrapper.findAll('button').find(button => button.text() === 'Hide dependencies')!.trigger('click')
    vi.mocked(thinkingApi.get).mockResolvedValue(deck(false))
    await expand(wrapper)
    expect(writable(wrapper)).toBe(false)
    superseded(deck(true))
    await flushPromises()
    expect(writable(wrapper)).toBe(false)
    expect(wrapper.find('form').exists()).toBe(false)
    wrapper.unmount()
  })

  it('drops a permission refresh that resolves after the route moved to another card', async () => {
    vi.mocked(thinkingApi.get).mockResolvedValue(deck(false))
    const wrapper = open(); await flushPromises()
    await expand(wrapper)
    let resolve!: (value: ReturnType<typeof deck>) => void
    vi.mocked(thinkingApi.get).mockReturnValueOnce(new Promise(r => { resolve = r }))
    await refresh(wrapper)
    vi.mocked(thinkingApi.get).mockResolvedValue(deck(false, 'card-b'))
    await wrapper.setProps({ cardId: 'card-b' }); await flushPromises()
    resolve(deck(true))
    await flushPromises()
    expect(wrapper.text()).toContain('Read-only')
    expect(writable(wrapper)).toBe(false)
    wrapper.unmount()
  })
})
