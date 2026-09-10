import { mount, flushPromises } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import ThinkingDeckPanel from '../../components/thinking/ThinkingDeckPanel.vue'
import CardDependencies from '../../components/thinking/CardDependencies.vue'
import { thinkingApi } from '../../api/thinkingApi'

vi.mock('../../api/thinkingApi', () => ({ thinkingApi: { get: vi.fn(), save: vi.fn() } }))
const props = { boardId: 'board-a', cardId: 'card-a' }
beforeEach(() => {
  vi.clearAllMocks()
  vi.mocked(thinkingApi.get).mockResolvedValue({ cardId: 'card-a', revision: 0, schemaVersion: 1, canWrite: true, layers: [] })
})

describe('ThinkingDeckPanel', () => {
  it('passes only current confirmed card write permission to dependencies across card reads', async () => {
    let resolve!: (value: Awaited<ReturnType<typeof thinkingApi.get>>) => void
    vi.mocked(thinkingApi.get).mockResolvedValueOnce({ cardId: 'card-a', revision: 0, schemaVersion: 1, canWrite: false, layers: [] })
    const wrapper = mount(ThinkingDeckPanel, { props, global: { stubs: { CardDependencies: true } } })
    await flushPromises()
    expect(wrapper.getComponent(CardDependencies).props('canWrite')).toBe(false)
    vi.mocked(thinkingApi.get).mockReturnValueOnce(new Promise(r => { resolve = r }))
    await wrapper.setProps({ cardId: 'restored-card' })
    expect(wrapper.findComponent(CardDependencies).exists()).toBe(false)
    resolve({ cardId: 'restored-card', revision: 0, schemaVersion: 1, canWrite: true, layers: [] })
    await flushPromises()
    expect(wrapper.getComponent(CardDependencies).props('canWrite')).toBe(true)
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
