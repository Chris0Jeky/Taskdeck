import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import ChatContextPicker from '../../../components/chat/ChatContextPicker.vue'
import { useSessionStore } from '../../../store/sessionStore'
import type { Memory } from '../../../types/workspaceInsights'

const api = vi.hoisted(() => ({ cards: vi.fn(), memories: vi.fn() }))
vi.mock('../../../api/cardsApi', () => ({ cardsApi: { getCards: api.cards } }))
vi.mock('../../../api/workspaceInsights', () => ({ workspaceInsightsApi: { getMemories: api.memories } }))
const memory = (id = 'm1', boardId = 'b1') => ({ id, boardId, title: `Private ${id}`, text: 'An uncertain assumption', status: 'unknown', revision: 3, archived: false }) as Memory
function setup() {
  const pinia = createPinia(); setActivePinia(pinia)
  const session = useSessionStore(); session.userId = 'actor'; session.token = 'token'
  return { session, wrapper: mount(ChatContextPicker, { props: { boardId: 'b1' }, global: { plugins: [pinia] } }) }
}
async function open(wrapper: ReturnType<typeof setup>['wrapper']) {
  await wrapper.get('button').trigger('click'); await flushPromises()
}
describe('explicit chat context', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    api.cards.mockResolvedValue([{ id: 'c1', boardId: 'b1', title: 'Chosen card' }])
    api.memories.mockResolvedValue([memory()])
  })
  it('loads only on request and sends saved references rather than private text', async () => {
    const { wrapper } = setup()
    expect(api.memories).not.toHaveBeenCalled()
    await open(wrapper)
    expect(api.memories).toHaveBeenCalledWith('b1')
    expect(wrapper.emitted('change')?.at(-1)).toEqual([null])
    await wrapper.get('select').setValue('c1')
    await wrapper.findAll('input')[0]!.setValue(true)
    await wrapper.findAll('input')[1]!.setValue(true)
    expect(wrapper.emitted('change')?.at(-1)).toEqual([{ cardId: 'c1', includeThinking: true, memories: [{ id: 'm1', revision: 3 }] }])
    await wrapper.get('select').setValue('')
    expect(wrapper.emitted('change')?.at(-1)).toEqual([{ cardId: null, includeThinking: false, memories: [{ id: 'm1', revision: 3 }] }])
  })
  it('limits explicit private selection to five saved current memories', async () => {
    api.memories.mockResolvedValue([...Array.from({ length: 6 }, (_, i) => memory(`m${i}`)), memory('foreign', 'b2'), { ...memory('archived'), archived: true }])
    const { wrapper } = setup(); await open(wrapper)
    const inputs = wrapper.findAll('input').slice(1)
    expect(inputs).toHaveLength(6)
    for (const input of inputs.slice(0, 5)) await input.setValue(true)
    expect(inputs[5]!.attributes('disabled')).toBeDefined()
  })
  it('clears private state on identity change and discards late reads', async () => {
    let resolve!: (value: Memory[]) => void
    api.memories.mockReturnValue(new Promise<Memory[]>(done => { resolve = done }))
    const { wrapper, session } = setup()
    await wrapper.get('button').trigger('click')
    session.userId = 'other'
    resolve([memory()]); await flushPromises()
    expect(wrapper.text()).not.toContain('Private m1')
    expect(wrapper.emitted('change')?.at(-1)).toEqual([null])
  })
  it('refresh failure clears a previously selected memory and allows retry', async () => {
    const { wrapper } = setup(); await open(wrapper)
    await wrapper.findAll('input')[1]!.setValue(true)
    api.memories.mockRejectedValueOnce(new Error('revoked'))
    await wrapper.findAll('button').at(-1)!.trigger('click'); await flushPromises()
    expect(wrapper.text()).not.toContain('Private m1')
    expect(wrapper.emitted('change')?.at(-1)).toEqual([null])
    expect(wrapper.get('[role="alert"]').text()).toContain('could not be loaded')
    await wrapper.findAll('button').at(-1)!.trigger('click'); await flushPromises()
    expect(wrapper.text()).toContain('Private m1')
  })
  it('does not retrieve real sources for demo sessions', async () => {
    const { wrapper, session } = setup(); session.isDemo = true; await flushPromises()
    expect(wrapper.find('button').exists()).toBe(false)
    expect(api.cards).not.toHaveBeenCalled(); expect(api.memories).not.toHaveBeenCalled()
  })
})
