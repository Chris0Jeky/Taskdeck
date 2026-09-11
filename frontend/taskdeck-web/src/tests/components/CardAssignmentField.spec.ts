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
  it('reports the in-flight save so the host cannot promise to discard it (#2981)', async () => {
    let finish!: (value: Card) => void
    vi.mocked(cardsApi.replaceAssignments).mockReturnValue(new Promise(resolve => { finish = resolve }))
    const wrapper = mount(CardAssignmentField, { props: { card, readOnly: false } })
    await flushPromises()
    expect(wrapper.emitted('saving-change')).toEqual([[false]])

    await wrapper.findAll('input')[0]!.setValue(true)
    await button(wrapper, 'Save assignments').trigger('click')
    expect(wrapper.emitted('saving-change')?.at(-1)).toEqual([true])
    expect(wrapper.text()).toContain('cannot be discarded')

    finish({ ...card, updatedAt: 'v2', assignments: [
      { userId: 'me', displayName: 'Owner', assignedAt: 'now', assignedByUserId: 'me' },
    ] })
    await flushPromises()
    expect(wrapper.emitted('saving-change')?.at(-1)).toEqual([false])
    expect(wrapper.text()).not.toContain('cannot be discarded')
  })

  it('reports the save as settled when it fails, keeping the draft (#2981)', async () => {
    let fail!: (reason: unknown) => void
    vi.mocked(cardsApi.replaceAssignments).mockReturnValue(new Promise((_resolve, reject) => { fail = reject }))
    const wrapper = mount(CardAssignmentField, { props: { card, readOnly: false } })
    await flushPromises()
    await wrapper.findAll('input')[1]!.setValue(true)
    await button(wrapper, 'Save assignments').trigger('click')
    expect(wrapper.emitted('saving-change')?.at(-1)).toEqual([true])

    fail({ response: { status: 500 } })
    await flushPromises()
    expect(wrapper.emitted('saving-change')?.at(-1)).toEqual([false])
    expect(wrapper.text()).toContain('Could not confirm assignment save')
    expect((wrapper.findAll('input')[1]!.element as HTMLInputElement).checked).toBe(true)
  })

  it('reports a new card as not saving when a delayed save is left behind (#2981)', async () => {
    vi.mocked(cardsApi.replaceAssignments).mockReturnValue(new Promise(() => {}))
    const wrapper = mount(CardAssignmentField, { props: { card, readOnly: false } })
    await flushPromises()
    await wrapper.findAll('input')[0]!.setValue(true)
    await button(wrapper, 'Save assignments').trigger('click')
    expect(wrapper.emitted('saving-change')?.at(-1)).toEqual([true])

    await wrapper.setProps({ card: { ...card, id: 'next' } }); await flushPromises()
    expect(wrapper.emitted('saving-change')?.at(-1)).toEqual([false])
  })

  /*
   * #2982. A downgrade to Viewer between the participant read and the PUT is a
   * settled fact, not an uncertain outcome: the user keeps read access, so
   * every read this field can make still succeeds and none of them is evidence
   * that writing is allowed again.
   */
  describe('revoked edit permission (#2982)', () => {
    async function downgradedDuringSave() {
      const wrapper = mount(CardAssignmentField, { props: { card, readOnly: false } })
      await flushPromises()
      await wrapper.findAll('input')[1]!.setValue(true)
      vi.mocked(cardsApi.replaceAssignments).mockRejectedValue({ response: { status: 403 } })
      await button(wrapper, 'Save assignments').trigger('click'); await flushPromises()
      return wrapper
    }

    it('explains the revoked permission and locks the write controls, keeping the draft', async () => {
      const wrapper = await downgradedDuringSave()
      expect(wrapper.text()).toContain('Your edit permission was revoked')
      expect(wrapper.text()).not.toContain('Could not confirm assignment save')
      expect(wrapper.find('fieldset').attributes('disabled')).toBeDefined()
      expect(button(wrapper, 'Save assignments').attributes('disabled')).toBeDefined()
      // Draft kept for reading, and the participant list stays readable.
      expect((wrapper.findAll('input')[1]!.element as HTMLInputElement).checked).toBe(true)
      expect(wrapper.text()).toContain('Viewer')
    })

    it('stays locked after a participant refresh a Viewer can still complete', async () => {
      const wrapper = await downgradedDuringSave()
      vi.mocked(cardsApi.getCard).mockResolvedValue({ ...card, updatedAt: 'v3' })
      await button(wrapper, 'Refresh current assignments').trigger('click'); await flushPromises()
      expect(vi.mocked(cardsApi.getParticipants)).toHaveBeenCalledTimes(2)
      expect(wrapper.text()).toContain('Your edit permission was revoked')
      expect(wrapper.find('fieldset').attributes('disabled')).toBeDefined()
      expect((wrapper.findAll('input')[1]!.element as HTMLInputElement).checked).toBe(true)
      /*
       * test-utils skips the dispatch on a disabled button, so clicking Save here
       * would only re-assert the attribute. Call the handler directly so the
       * `if (locked.value) return` guard inside save() is the thing under test.
       */
      await (wrapper.vm as unknown as { save: () => Promise<void> }).save(); await flushPromises()
      expect(cardsApi.replaceAssignments).toHaveBeenCalledTimes(1)
    })

    it('still lets the draft be cancelled, so the host is not left permanently dirty', async () => {
      const wrapper = await downgradedDuringSave()
      expect(wrapper.emitted('dirty-change')?.at(-1)).toEqual([true])
      const cancel = button(wrapper, 'Cancel assignment changes')
      /*
       * The attribute alone proves nothing here: Cancel used to sit inside
       * `<fieldset :disabled="locked">`, which disabled it in a real browser
       * WITHOUT setting its own attribute — and jsdom does not reflect ancestor
       * disabling onto `button.disabled`, so the attribute check and the click
       * both passed while the control was dead on screen. Assert the ancestry.
       */
      expect(cancel.attributes('disabled')).toBeUndefined()
      expect(cancel.element.closest('fieldset[disabled]')).toBeNull()
      expect(button(wrapper, 'Clear').element.closest('fieldset[disabled]')).toBeNull()
      await cancel.trigger('click'); await flushPromises()
      expect(wrapper.emitted('dirty-change')?.at(-1)).toEqual([false])
      expect((wrapper.findAll('input')[1]!.element as HTMLInputElement).checked).toBe(false)
      expect(cardsApi.replaceAssignments).toHaveBeenCalledTimes(1)
      // Cancelling the draft is not regaining permission.
      expect(wrapper.find('fieldset').attributes('disabled')).toBeDefined()
      expect(wrapper.text()).toContain('Your edit permission was revoked')
      expect(button(wrapper, 'Save assignments').attributes('disabled')).toBeDefined()
    })

    it('keeps the draft dismissible once the board refresh confirms the downgrade', async () => {
      const wrapper = await downgradedDuringSave()
      // The board refetch catches up and reports canWrite:false.
      await wrapper.setProps({ readOnly: true }); await flushPromises()
      expect(button(wrapper, 'Save assignments')).toBeUndefined()
      const cancel = button(wrapper, 'Cancel assignment changes')
      expect(cancel.attributes('disabled')).toBeUndefined()
      expect(cancel.element.closest('fieldset[disabled]')).toBeNull()
      await cancel.trigger('click'); await flushPromises()
      expect(wrapper.emitted('dirty-change')?.at(-1)).toEqual([false])
      expect(cardsApi.replaceAssignments).toHaveBeenCalledTimes(1)
    })

    it('surfaces a refresh that fails after the refusal instead of swallowing it', async () => {
      const wrapper = await downgradedDuringSave()
      vi.mocked(cardsApi.getCard).mockRejectedValue({ response: { status: 500 } })
      await button(wrapper, 'Refresh current assignments').trigger('click'); await flushPromises()
      expect(wrapper.text()).toContain('refresh also failed')
      expect(wrapper.text()).toContain('may be out of date')
      // The stale promise that the shown assignees are current must be gone.
      expect(wrapper.text()).not.toContain('the current assignees stay readable')
      expect(wrapper.find('fieldset').attributes('disabled')).toBeDefined()
      /*
       * The third route to a stranded draft: a failed refresh sets needsRefresh,
       * which used to disable Clear and Cancel too. If read access is gone for
       * good, or the network stays down, that leaves the host permanently dirty
       * with no way out but discarding or reopening the whole editor.
       */
      const cancel = button(wrapper, 'Cancel assignment changes')
      expect(cancel.attributes('disabled')).toBeUndefined()
      await cancel.trigger('click'); await flushPromises()
      expect(wrapper.emitted('dirty-change')?.at(-1)).toEqual([false])
    })

    it('unlocks only when the parent reports write permission again', async () => {
      const wrapper = await downgradedDuringSave()
      await wrapper.setProps({ readOnly: true }); await flushPromises()
      await wrapper.setProps({ readOnly: false }); await flushPromises()
      expect(wrapper.text()).not.toContain('Your edit permission was revoked')
      expect(wrapper.find('fieldset').attributes('disabled')).toBeUndefined()
      vi.mocked(cardsApi.replaceAssignments).mockResolvedValue({ ...card, updatedAt: 'v2' })
      await button(wrapper, 'Save assignments').trigger('click'); await flushPromises()
      expect(cardsApi.replaceAssignments).toHaveBeenLastCalledWith('board', 'card', ['viewer'], 'v1')
    })

    it('ignores a 403 that settles after a newer request superseded it', async () => {
      let fail!: (reason: unknown) => void
      vi.mocked(cardsApi.replaceAssignments).mockReturnValue(new Promise((_resolve, reject) => { fail = reject }))
      const wrapper = mount(CardAssignmentField, { props: { card, readOnly: false } })
      await flushPromises()
      await wrapper.findAll('input')[0]!.setValue(true)
      await button(wrapper, 'Save assignments').trigger('click')
      await wrapper.setProps({ card: { ...card, id: 'next' } }); await flushPromises()
      fail({ response: { status: 403 } }); await flushPromises()
      expect(wrapper.text()).not.toContain('Your edit permission was revoked')
      expect(wrapper.find('fieldset').attributes('disabled')).toBeUndefined()
    })

    it('leaves an ineligible-participant rejection refreshable and unlockable', async () => {
      const wrapper = mount(CardAssignmentField, { props: { card, readOnly: false } })
      await flushPromises()
      await wrapper.findAll('input')[1]!.setValue(true)
      vi.mocked(cardsApi.replaceAssignments).mockRejectedValue({ response: { status: 400 } })
      await button(wrapper, 'Save assignments').trigger('click'); await flushPromises()
      expect(wrapper.text()).toContain('no longer eligible')
      expect(wrapper.find('fieldset').attributes('disabled')).toBeDefined()
      vi.mocked(cardsApi.getCard).mockResolvedValue({ ...card, updatedAt: 'v3' })
      await button(wrapper, 'Refresh current assignments').trigger('click'); await flushPromises()
      expect(wrapper.text()).not.toContain('no longer eligible')
      expect(wrapper.find('fieldset').attributes('disabled')).toBeUndefined()
    })
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
