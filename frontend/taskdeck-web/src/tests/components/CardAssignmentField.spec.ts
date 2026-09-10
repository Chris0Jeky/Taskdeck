import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import CardAssignmentField from '../../components/board/CardAssignmentField.vue'
import { cardsApi } from '../../api/cardsApi'
import type { Card } from '../../types/board'

vi.mock('../../api/cardsApi', () => ({ cardsApi: {
  getParticipants: vi.fn(), replaceAssignments: vi.fn(), getCard: vi.fn(),
} }))
vi.mock('../../store/sessionStore', () => ({ useSessionStore: () => ({ userId: 'me' }) }))
const card: Card = {
  id: 'card', boardId: 'board', columnId: 'col', title: 'Draft', description: '', labels: [],
  isBlocked: false, blockReason: null, dueDate: null, position: 0, createdAt: 'old', updatedAt: 'v1', assignments: [],
}
function button(wrapper: ReturnType<typeof mount>, text: string) {
  return wrapper.findAll('button').find(b => b.text() === text)!
}
describe('CardAssignmentField', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(cardsApi.getParticipants).mockResolvedValue([
      { userId: 'me', displayName: 'Owner' }, { userId: 'viewer', displayName: 'Viewer' },
    ])
  })
  it('saves an explicit multi-set and allows clear/cancel without mutation', async () => {
    const wrapper = mount(CardAssignmentField, { props: { card, readOnly: false } })
    await flushPromises()
    await wrapper.findAll('input')[0]!.setValue(true)
    await wrapper.findAll('input')[1]!.setValue(true)
    await button(wrapper, 'Cancel assignment changes').trigger('click')
    expect(cardsApi.replaceAssignments).not.toHaveBeenCalled()
    expect((wrapper.findAll('input')[0]!.element as HTMLInputElement).checked).toBe(false)
    await wrapper.findAll('input')[0]!.setValue(true)
    await wrapper.findAll('input')[1]!.setValue(true)
    vi.mocked(cardsApi.replaceAssignments).mockResolvedValue({ ...card, updatedAt: 'v2', assignments: [
      { userId: 'me', displayName: 'Owner', assignedAt: 'now', assignedByUserId: 'me' },
      { userId: 'viewer', displayName: 'Viewer', assignedAt: 'now', assignedByUserId: 'me' },
    ] })
    await button(wrapper, 'Save assignments').trigger('click'); await flushPromises()
    expect(cardsApi.replaceAssignments).toHaveBeenCalledWith('board', 'card', ['me', 'viewer'], 'v1')
    await button(wrapper, 'Clear').trigger('click')
    await button(wrapper, 'Save assignments').trigger('click'); await flushPromises()
    expect(cardsApi.replaceAssignments).toHaveBeenLastCalledWith('board', 'card', [], 'v2')
  })
  it('keeps a conflicted selection and refreshes the version before explicit retry', async () => {
    const wrapper = mount(CardAssignmentField, { props: { card, readOnly: false } })
    await flushPromises(); await wrapper.findAll('input')[1]!.setValue(true)
    vi.mocked(cardsApi.replaceAssignments).mockRejectedValue({ response: { status: 409 } })
    await button(wrapper, 'Save assignments').trigger('click'); await flushPromises()
    expect(wrapper.text()).toContain('kept draft')
    expect((wrapper.findAll('input')[1]!.element as HTMLInputElement).checked).toBe(true)
    vi.mocked(cardsApi.getCard).mockResolvedValue({ ...card, updatedAt: 'v3' })
    await button(wrapper, 'Refresh current assignments').trigger('click'); await flushPromises()
    expect((wrapper.findAll('input')[1]!.element as HTMLInputElement).checked).toBe(true)
    vi.mocked(cardsApi.replaceAssignments).mockResolvedValue({ ...card, updatedAt: 'v4' })
    await button(wrapper, 'Save assignments').trigger('click'); await flushPromises()
    expect(cardsApi.replaceAssignments).toHaveBeenLastCalledWith('board', 'card', ['viewer'], 'v3')
  })
  it('ignores a delayed old-card save after selecting another card', async () => {
    let finish!: (value: Card) => void
    vi.mocked(cardsApi.replaceAssignments).mockReturnValue(new Promise(resolve => { finish = resolve }))
    const wrapper = mount(CardAssignmentField, { props: { card, readOnly: false } })
    await flushPromises(); await wrapper.findAll('input')[0]!.setValue(true)
    await button(wrapper, 'Save assignments').trigger('click')
    await wrapper.setProps({ card: { ...card, id: 'next' } }); await flushPromises()
    finish({ ...card, updatedAt: 'late' }); await flushPromises()
    expect(wrapper.emitted('saved')).toBeUndefined()
    expect((wrapper.findAll('input')[0]!.element as HTMLInputElement).checked).toBe(false)
  })
  it('retains a selection across realtime prop updates and prevents readonly changes', async () => {
    const wrapper = mount(CardAssignmentField, { props: { card, readOnly: false } })
    await flushPromises(); await wrapper.findAll('input')[0]!.setValue(true)
    await wrapper.setProps({ card: { ...card, updatedAt: 'remote' } })
    expect((wrapper.findAll('input')[0]!.element as HTMLInputElement).checked).toBe(true)
    await wrapper.setProps({ readOnly: true })
    expect(wrapper.find('fieldset').attributes('disabled')).toBeDefined()
    expect(button(wrapper, 'Save assignments')).toBeUndefined()
  })
})
