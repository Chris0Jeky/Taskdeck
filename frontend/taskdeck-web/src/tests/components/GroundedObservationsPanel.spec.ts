import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { enableAutoUnmount, flushPromises, mount } from '@vue/test-utils'
import { reactive } from 'vue'
import GroundedObservationsPanel from '../../components/workspace/GroundedObservationsPanel.vue'

enableAutoUnmount(afterEach)
const mocks = vi.hoisted(() => ({ cards: vi.fn(), source: vi.fn(), generate: vi.fn() }))
const session = reactive({ userId: 'user-1' })
vi.mock('../../store/sessionStore', () => ({ useSessionStore: () => session }))
vi.mock('../../api/cardsApi', () => ({ cardsApi: { getCards: mocks.cards } }))
vi.mock('../../api/workspaceInsights', () => ({ workspaceInsightsApi: { observationSource: mocks.source, generateObservations: mocks.generate } }))
const source = { cardId: 'card-1', title: 'Investigate rollout', text: 'Title: Investigate rollout', fingerprint: 'A'.repeat(64), truncated: false }
function open() { return mount(GroundedObservationsPanel, { props: { boardId: 'board-1', disabled: false } }) }
function button(wrapper: ReturnType<typeof open>, name: string) { return wrapper.findAll('button').find(x => x.text() === name)! }
async function preview(wrapper: ReturnType<typeof open>) {
  await button(wrapper, 'Choose a card').trigger('click'); await flushPromises()
  await wrapper.get('select').setValue('card-1')
  await button(wrapper, 'Preview current evidence').trigger('click'); await flushPromises()
}
beforeEach(() => {
  vi.clearAllMocks(); session.userId = 'user-1'
  mocks.cards.mockResolvedValue([{ id: 'card-1', title: source.title }])
  mocks.source.mockResolvedValue(source)
  mocks.generate.mockResolvedValue([{ id: 'question-1' }])
})

describe('GroundedObservationsPanel', () => {
  it('preserves the concurrent-save recovery outcome instead of claiming the source changed', async () => {
    const wrapper = open(); await preview(wrapper)
    const message = 'Another request changed your question results. This request saved no observations. Reload Quiet insights to read the current results. Model usage was already accounted for; analyzing again uses budget again.'
    mocks.generate.mockRejectedValue({ response: { data: { errorCode: 'Conflict', message } } })
    await button(wrapper, 'Analyze this evidence with model').trigger('click'); await flushPromises()
    expect(wrapper.get('[role="alert"]').text()).toBe(message)
    expect(wrapper.text()).not.toContain('The source changed')
    expect(mocks.generate).toHaveBeenCalledOnce()
    expect(wrapper.find('pre').exists()).toBe(false)
    expect(wrapper.emitted('generated')).toBeUndefined()
  })

  it('waits for explicit selection and preview, then submits exactly that evidence once', async () => {
    const wrapper = open(); await flushPromises()
    expect(mocks.cards).not.toHaveBeenCalled(); expect(mocks.generate).not.toHaveBeenCalled()
    await preview(wrapper)
    expect(wrapper.get('pre').text()).toBe(source.text)
    expect(mocks.generate).not.toHaveBeenCalled()
    let finish!: (value: unknown[]) => void
    mocks.generate.mockReturnValue(new Promise(resolve => { finish = resolve }))
    await button(wrapper, 'Analyze this evidence with model').trigger('click')
    await button(wrapper, 'Analyze this evidence with model').trigger('click')
    expect(mocks.generate).toHaveBeenCalledOnce()
    expect(mocks.generate).toHaveBeenCalledWith('board-1', source)
    expect(wrapper.get('select').attributes('disabled')).toBeDefined()
    finish([{ id: 'question-1' }]); await flushPromises()
    expect(wrapper.emitted('generated')).toHaveLength(1)
    expect(wrapper.find('pre').exists()).toBe(false)
    expect(wrapper.text()).toContain('1 model questions saved')
  })
  it('removes evidence after an uncertain write and requires a fresh preview', async () => {
    const wrapper = open(); await preview(wrapper)
    mocks.generate.mockRejectedValue(new Error('The source changed.'))
    await button(wrapper, 'Analyze this evidence with model').trigger('click'); await flushPromises()
    expect(wrapper.get('[role="alert"]').text()).toContain('source changed')
    expect(wrapper.find('pre').exists()).toBe(false)
    expect(button(wrapper, 'Preview current evidence').attributes('disabled')).toBeUndefined()
  })
  it('discards an old board preview response', async () => {
    let finish!: (value: typeof source) => void
    mocks.source.mockReturnValue(new Promise(resolve => { finish = resolve }))
    const wrapper = open()
    await button(wrapper, 'Choose a card').trigger('click'); await flushPromises()
    await wrapper.get('select').setValue('card-1')
    await button(wrapper, 'Preview current evidence').trigger('click')
    await wrapper.setProps({ boardId: 'board-2' })
    finish(source); await flushPromises()
    expect(wrapper.find('pre').exists()).toBe(false)
    expect(wrapper.find('select').exists()).toBe(false)
  })
  it('clears the prior account evidence and ignores its pending generation result', async () => {
    let finish!: (value: unknown[]) => void
    const wrapper = open(); await preview(wrapper)
    mocks.generate.mockReturnValue(new Promise(resolve => { finish = resolve }))
    await button(wrapper, 'Analyze this evidence with model').trigger('click')
    session.userId = 'user-2'; await flushPromises()
    finish([{ id: 'private-question' }]); await flushPromises()
    expect(wrapper.find('pre').exists()).toBe(false)
    expect(wrapper.emitted('generated')).toBeUndefined()
    expect(wrapper.text()).not.toContain('saved for your review')
  })
  it('shows empty sources and recovers a failed source read', async () => {
    mocks.cards.mockResolvedValueOnce([])
    const wrapper = open(); await button(wrapper, 'Choose a card').trigger('click'); await flushPromises()
    expect(wrapper.text()).toContain('no cards to analyze')
    await button(wrapper, 'Refresh card choices').trigger('click'); await flushPromises()
    await wrapper.get('select').setValue('card-1')
    mocks.source.mockRejectedValueOnce(new Error('Access removed'))
    await button(wrapper, 'Preview current evidence').trigger('click'); await flushPromises()
    expect(wrapper.get('[role="alert"]').text()).toContain('Access removed')
    expect(wrapper.find('pre').exists()).toBe(false)
    await button(wrapper, 'Preview current evidence').trigger('click'); await flushPromises()
    expect(wrapper.get('pre').text()).toBe(source.text)
  })
})
