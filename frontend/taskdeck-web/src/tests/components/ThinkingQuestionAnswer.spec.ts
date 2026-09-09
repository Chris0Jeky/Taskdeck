import { mount, flushPromises } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import ThinkingQuestionAnswer from '../../components/thinking/ThinkingQuestionAnswer.vue'
import ThinkingDeckPanel from '../../components/thinking/ThinkingDeckPanel.vue'
import { thinkingApi } from '../../api/thinkingApi'
import type { Memory } from '../../types/workspaceInsights'

vi.mock('../../api/thinkingApi', () => ({ thinkingApi: { get: vi.fn(), save: vi.fn(), getAnswer: vi.fn(), answer: vi.fn() } }))
const props = { boardId: 'board', cardId: 'card', layerId: 'question', revision: 3, sourceReady: true }
const memory: Memory = { id: 'memory', boardId: 'board', title: 'Question', text: 'Only for me', originalText: 'Only for me', originalEvidence: 'Server source', status: 'unknown', archived: false, revision: 1, createdAt: '2026-09-08', history: [] }
const global = { stubs: { RouterLink: { template: '<a><slot /></a>' } } }
beforeEach(() => { vi.clearAllMocks(); vi.mocked(thinkingApi.getAnswer).mockResolvedValue(null) })

describe('ThinkingQuestionAnswer', () => {
  it('keeps explicit answers privately with a saved source revision', async () => {
    const wrapper = mount(ThinkingQuestionAnswer, { props, global })
    expect(thinkingApi.getAnswer).not.toHaveBeenCalled()
    await wrapper.get('button').trigger('click'); await flushPromises()
    await wrapper.get('[aria-label="Private answer"]').setValue('Only for me')
    await wrapper.get('select').setValue('unknown')
    vi.mocked(thinkingApi.answer).mockResolvedValue(memory)
    await wrapper.findAll('button').find(x => x.text() === 'Keep answer privately')!.trigger('click'); await flushPromises()
    expect(thinkingApi.answer).toHaveBeenCalledWith('board', 'card', 'question', 3, 'Only for me', 'unknown')
    expect(thinkingApi.save).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('Kept in your private memory')
    expect(wrapper.text()).toContain('Review or correct in private memory')
    expect(wrapper.emitted('dirty-change')?.at(-1)).toEqual([false])
  })

  it('retains an answer draft on conflict and while hiding the panel', async () => {
    const wrapper = mount(ThinkingQuestionAnswer, { props, global })
    await wrapper.get('button').trigger('click'); await flushPromises()
    await wrapper.get('textarea').setValue('My draft')
    vi.mocked(thinkingApi.answer).mockRejectedValue({ response: { status: 409 } })
    await wrapper.findAll('button').find(x => x.text() === 'Keep answer privately')!.trigger('click'); await flushPromises()
    expect(wrapper.text()).toContain('Your draft is kept')
    await wrapper.get('button').trigger('click'); await wrapper.get('button').trigger('click')
    expect((wrapper.get('textarea').element as HTMLTextAreaElement).value).toBe('My draft')
    expect(wrapper.emitted('dirty-change')?.at(-1)).toEqual([true])
  })

  it('requires saved shared source and loads when it becomes ready', async () => {
    const wrapper = mount(ThinkingQuestionAnswer, { props: { ...props, sourceReady: false }, global })
    await wrapper.get('button').trigger('click')
    expect(thinkingApi.getAnswer).not.toHaveBeenCalled()
    await wrapper.get('textarea').setValue('Keep this')
    expect(wrapper.findAll('button').find(x => x.text() === 'Keep answer privately')!.attributes('disabled')).toBeDefined()
    await wrapper.setProps({ revision: 4, sourceReady: true }); await flushPromises()
    expect(thinkingApi.getAnswer).toHaveBeenCalledOnce()
    expect((wrapper.get('textarea').element as HTMLTextAreaElement).value).toBe('Keep this')
  })

  it('lets viewers keep private drafts without enabling shared editing', async () => {
    vi.mocked(thinkingApi.get).mockResolvedValue({ cardId: 'card', canWrite: false, revision: 3, schemaVersion: 1, layers: [{ id: 'question', kind: 'question', title: 'Shared question', body: '', items: [], selectedOptionId: null }] })
    const wrapper = mount(ThinkingDeckPanel, { props: { boardId: 'board', cardId: 'card' }, global })
    await flushPromises()
    const open = wrapper.findAll('button').find(x => x.text() === 'Your private answer')!
    expect(open.element.closest('fieldset[disabled]')).toBeNull()
    await open.trigger('click'); await flushPromises()
    await wrapper.get('[aria-label="Private answer"]').setValue('A private draft')
    expect(wrapper.emitted('dirty-change')?.at(-1)).toEqual([true])
    await wrapper.findAll('button').find(x => x.text() === 'Path')!.trigger('click')
    expect((wrapper.get('[aria-label="Private answer"]').element as HTMLTextAreaElement).value).toBe('A private draft')
    expect(wrapper.get('fieldset[aria-label="Shared thinking layer 1"]').attributes('disabled')).toBeDefined()
  })
})
