import { describe, it, expect, afterEach, beforeEach, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { nextTick, reactive } from 'vue'
import { setActivePinia, createPinia } from 'pinia'
import CardModal from '../../components/board/CardModal.vue'
import { cardsApi } from '../../api/cardsApi'
import { useBoardStore } from '../../store/boardStore'
import { useSessionStore } from '../../store/sessionStore'
import type { Card, Label } from '../../types/board'

/*
 * #2981. `CardAssignmentField` submits the assignment PUT and then only guards
 * the *response* with a generation counter. Unmounting the field — which is what
 * every close path does — increments that counter, so the receipt is dropped
 * while the mutation the server already received still commits. The editor's
 * "Discard changes" confirmation therefore promised a discard for a change that
 * was already on its way to the database.
 *
 * These specs pin the truthful behaviour: while the PUT is in flight no close,
 * discard or navigation affordance may claim the change was discarded.
 */

vi.mock('../../api/cardsApi', () => ({ cardsApi: {
  getCards: vi.fn().mockResolvedValue([]),
  getParticipants: vi.fn(),
  replaceAssignments: vi.fn(),
  getCard: vi.fn(),
  previewDetach: vi.fn().mockResolvedValue({ cardId: 'card-1', expectedUpdatedAt: 'v1', expectedChildrenFingerprint: 'v1:fixed', children: [] }),
} }))
vi.mock('../../store/boardStore', () => ({ useBoardStore: vi.fn() }))
vi.mock('../../store/sessionStore', () => ({ useSessionStore: vi.fn() }))

function createDeferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise
    reject = rejectPromise
  })
  return { promise, resolve, reject }
}

const labels: Label[] = []
const card: Card = {
  id: 'card-1', boardId: 'board-1', columnId: 'column-1', title: 'Assignment responsibility',
  description: '', labels: [], isBlocked: false, blockReason: null, dueDate: null, position: 0,
  createdAt: '2026-09-10T00:00:00.000Z', updatedAt: 'v1', assignments: [],
}
const savedCard: Card = {
  ...card, updatedAt: 'v2',
  assignments: [{ userId: 'user-2', displayName: 'Teammate', assignedAt: '2026-09-11T00:00:00.000Z', assignedByUserId: 'user-1' }],
}

type Wrapper = ReturnType<typeof mount>

function fieldButton(wrapper: Wrapper, text: string) {
  return wrapper.findAll('button').find(candidate => candidate.text() === text)
}

function discardConfirmButton() {
  return document.body.querySelector('[data-testid="card-discard-confirm"]') as HTMLButtonElement | null
}

function savePendingDismissButton() {
  return document.body.querySelector('[data-testid="card-assignment-save-pending-dismiss"]') as HTMLButtonElement | null
}

function noticeText() {
  return savePendingDismissButton()?.closest('[role="dialog"]')?.textContent ?? ''
}

function archiveConfirmationButton() {
  return Array.from(document.body.querySelectorAll('button')).find(button => button.textContent === 'Confirm archive')
}

describe('CardModal assignment save in flight (#2981)', () => {
  let mockStore: Record<string, unknown>

  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    vi.mocked(cardsApi.getParticipants).mockResolvedValue([{ userId: 'user-2', displayName: 'Teammate' }])
    mockStore = {
      currentBoard: { id: 'board-1', canWrite: true, isArchived: false },
      currentBoardCards: [card],
      updateCard: vi.fn().mockResolvedValue(card),
      setCardArchived: vi.fn(),
      deleteCard: vi.fn().mockResolvedValue(undefined),
      fetchCardComments: vi.fn().mockResolvedValue([]),
      fetchCardProvenance: vi.fn().mockResolvedValue(null),
      getCardComments: vi.fn().mockReturnValue([]),
      createCardComment: vi.fn().mockResolvedValue(undefined),
      updateCardComment: vi.fn().mockResolvedValue(undefined),
      deleteCardComment: vi.fn().mockResolvedValue(undefined),
      fetchBoard: vi.fn().mockResolvedValue(undefined),
      editingCardId: null,
      setEditingCard: vi.fn(),
    }
    vi.mocked(useBoardStore).mockReturnValue(mockStore as never)
    vi.mocked(useSessionStore).mockReturnValue({ userId: 'user-1' } as never)
  })

  // These specs mount into the body and assert on teleported dialogs, so no
  // test may inherit another one's DOM.
  afterEach(() => {
    document.body.innerHTML = ''
  })

  /**
   * Mount the editor, change the assignment selection and submit it with a PUT
   * that never settles until the test resolves it.
   */
  async function mountWithPendingAssignmentSave(presentation: 'modal' | 'inspector') {
    const deferred = createDeferred<Card>()
    vi.mocked(cardsApi.replaceAssignments).mockReturnValue(deferred.promise)
    const wrapper = mount(CardModal, {
      props: { card, isOpen: true, labels, presentation },
      attachTo: document.body,
    })
    await flushPromises()

    const assignments = wrapper.get('[aria-label="Card assignments"]')
    await assignments.findAll('input[type="checkbox"]')[0]!.setValue(true)
    await fieldButton(wrapper, 'Save assignments')!.trigger('click')
    await nextTick()

    expect(cardsApi.replaceAssignments).toHaveBeenCalledWith('board-1', 'card-1', ['user-2'], 'v1')
    return { wrapper, deferred }
  }

  for (const presentation of ['modal', 'inspector'] as const) {
    const surface = presentation === 'modal' ? 'Legacy' : 'Paper'

    it(`${surface}: never offers a discard that would misrepresent an in-flight assignment save`, async () => {
      const { wrapper, deferred } = await mountWithPendingAssignmentSave(presentation)

      // The close request arrives while the PUT is still unanswered.
      await wrapper.get('[aria-label="Close card editor"]').trigger('click')
      await nextTick()

      // Confirming the discard — the exact sequence from the receipt — must not
      // close the editor behind an unanswered mutation.
      discardConfirmButton()?.click()
      await nextTick()
      expect(wrapper.emitted('close')).toBeUndefined()

      // And the editor must not have offered that discard at all; it says what
      // is actually true instead.
      expect(discardConfirmButton()).toBeNull()
      expect(savePendingDismissButton()).not.toBeNull()
      expect(noticeText()).toContain('already sent to the server')
      expect(noticeText()).toContain('cannot be discarded or cancelled')

      // The save the user was never allowed to "discard" commits, and the
      // editor shows the committed state instead of a false discard.
      deferred.resolve(savedCard)
      await flushPromises()
      expect(wrapper.emitted('close')).toBeUndefined()
      expect(savePendingDismissButton()).toBeNull()
      expect(mockStore.currentBoardCards).toEqual([savedCard])

      wrapper.unmount()
    })

    it(`${surface}: refuses Escape, the backdrop and the header while the save is unanswered, then lets them through`, async () => {
      const { wrapper, deferred } = await mountWithPendingAssignmentSave(presentation)

      // The editor's own Escape binding on the dialog element.
      const viewport = wrapper.get('.card-modal-viewport')
      await viewport.trigger('keydown', { key: 'Escape' })
      await nextTick()
      expect(wrapper.emitted('close')).toBeUndefined()
      expect(savePendingDismissButton()).not.toBeNull()

      // The shared Escape stack, with the notice dismissed first so the editor's
      // handler is the one on top.
      savePendingDismissButton()!.click()
      await nextTick()
      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))
      await nextTick()
      expect(wrapper.emitted('close')).toBeUndefined()
      expect(savePendingDismissButton()).not.toBeNull()

      // The backdrop (a self-click on the viewport).
      savePendingDismissButton()!.click()
      await nextTick()
      await viewport.trigger('click')
      await nextTick()
      expect(wrapper.emitted('close')).toBeUndefined()
      expect(savePendingDismissButton()).not.toBeNull()
      expect(discardConfirmButton()).toBeNull()

      // Settlement withdraws the notice and restores the controls: the card is
      // committed, nothing is dirty, and the header close now simply closes.
      deferred.resolve(savedCard)
      await flushPromises()
      expect(savePendingDismissButton()).toBeNull()
      expect(mockStore.currentBoardCards).toEqual([savedCard])
      expect(wrapper.get('[aria-label="Card assignments"]').text()).toContain('Teammate')

      await wrapper.get('[aria-label="Close card editor"]').trigger('click')
      await nextTick()
      expect(discardConfirmButton()).toBeNull()
      expect(wrapper.emitted('close')).toHaveLength(1)

      wrapper.unmount()
    })

    it(`${surface}: keeps the draft and the editor open when the delayed save fails`, async () => {
      const { wrapper, deferred } = await mountWithPendingAssignmentSave(presentation)

      await wrapper.get('[aria-label="Close card editor"]').trigger('click')
      await nextTick()
      expect(savePendingDismissButton()).not.toBeNull()

      deferred.reject({ response: { status: 500 } })
      await flushPromises()

      // The failure is reported, the draft is kept and the notice is gone.
      expect(savePendingDismissButton()).toBeNull()
      const assignments = wrapper.get('[aria-label="Card assignments"]')
      expect(assignments.text()).toContain('Could not confirm assignment save')
      expect((assignments.findAll('input[type="checkbox"]')[0]!.element as HTMLInputElement).checked).toBe(true)
      expect(mockStore.currentBoardCards).toEqual([card])
      expect(wrapper.emitted('close')).toBeUndefined()

      // The change is now genuinely un-submitted, so the ordinary discard
      // confirmation is offered again and honoured.
      await wrapper.get('[aria-label="Close card editor"]').trigger('click')
      await nextTick()
      expect(discardConfirmButton()).not.toBeNull()
      discardConfirmButton()!.click()
      await nextTick()
      expect(wrapper.emitted('close')).toHaveLength(1)

      wrapper.unmount()
    })

    it(`${surface}: replaces an already-open discard confirmation when a save starts behind it`, async () => {
      const deferred = createDeferred<Card>()
      vi.mocked(cardsApi.replaceAssignments).mockReturnValue(deferred.promise)
      const wrapper = mount(CardModal, {
        props: { card, isOpen: true, labels, presentation },
        attachTo: document.body,
      })
      await flushPromises()

      // A card-field draft opens the discard confirmation first.
      await wrapper.get('#card-title').setValue('Unsaved title')
      await wrapper.get('[aria-label="Close card editor"]').trigger('click')
      await nextTick()
      expect(discardConfirmButton()).not.toBeNull()

      // The assignment save starts behind it.
      const assignments = wrapper.get('[aria-label="Card assignments"]')
      await assignments.findAll('input[type="checkbox"]')[0]!.setValue(true)
      await fieldButton(wrapper, 'Save assignments')!.trigger('click')
      await nextTick()

      expect(discardConfirmButton()).toBeNull()
      expect(savePendingDismissButton()).not.toBeNull()
      expect(wrapper.emitted('close')).toBeUndefined()

      deferred.resolve(savedCard)
      await flushPromises()
      expect(savePendingDismissButton()).toBeNull()
      expect((wrapper.get('#card-title').element as HTMLInputElement).value).toBe('Unsaved title')
      expect(wrapper.emitted('close')).toBeUndefined()

      wrapper.unmount()
    })
  }

  /*
   * #2997. Archive recovery ("Refresh card state") is a close: it drops this
   * editor and refetches the board. It was the one close path that never
   * reached `handleClose`, so it walked out from under an unanswered PUT
   * without the truthful notice every other path gives.
   */
  it('refuses archive recovery while an assignment save is unanswered', async () => {
    vi.mocked(cardsApi.previewDetach).mockRejectedValueOnce(new Error('preview unavailable'))
    const wrapper = mount(CardModal, {
      props: { card, isOpen: true, labels, presentation: 'modal' },
      attachTo: document.body,
    })
    await flushPromises()

    // Fail an archive first, so the page-level recovery control is on screen.
    await fieldButton(wrapper, 'Archive card')!.trigger('click')
    await flushPromises()
    expect(fieldButton(wrapper, 'Refresh card state')).toBeDefined()

    // Then submit an assignment change the server has not answered.
    const deferred = createDeferred<Card>()
    vi.mocked(cardsApi.replaceAssignments).mockReturnValue(deferred.promise)
    const assignments = wrapper.get('[aria-label="Card assignments"]')
    await assignments.findAll('input[type="checkbox"]')[0]!.setValue(true)
    await fieldButton(wrapper, 'Save assignments')!.trigger('click')
    await nextTick()

    await fieldButton(wrapper, 'Refresh card state')!.trigger('click')
    await flushPromises()

    expect(wrapper.emitted('close')).toBeUndefined()
    expect(mockStore.fetchBoard).not.toHaveBeenCalled()
    expect(discardConfirmButton()).toBeNull()
    expect(savePendingDismissButton()).not.toBeNull()
    expect(noticeText()).toContain('already sent to the server')

    deferred.resolve(savedCard)
    await flushPromises()
    expect(wrapper.emitted('close')).toBeUndefined()

    // The refused refresh was dropped with the close it asked for, so the next,
    // unrelated close is an ordinary close and not a stale board refetch.
    await wrapper.get('[aria-label="Close card editor"]').trigger('click')
    await flushPromises()
    expect(wrapper.emitted('close')).toHaveLength(1)
    expect(mockStore.fetchBoard).not.toHaveBeenCalled()

    wrapper.unmount()
  })

  it('keeps the lifecycle control frozen after an archive settles before an assignment save', async () => {
    const archive = createDeferred<void>()
    const assignment = createDeferred<Card>()
    vi.mocked((mockStore as { setCardArchived: ReturnType<typeof vi.fn> }).setCardArchived).mockReturnValue(archive.promise)
    vi.mocked(cardsApi.replaceAssignments).mockReturnValue(assignment.promise)
    const wrapper = mount(CardModal, {
      props: { card, isOpen: true, labels, presentation: 'modal' },
      attachTo: document.body,
    })
    await flushPromises()

    // Start the archive while the editor is clean, then submit an assignment
    // edit while that lifecycle request still owns the active card version.
    await fieldButton(wrapper, 'Archive card')!.trigger('click')
    await flushPromises()
    archiveConfirmationButton()!.click()
    await flushPromises()
    expect(mockStore.setCardArchived).toHaveBeenCalledWith('board-1', 'card-1', true, 'v1', 'v1:fixed')

    const assignments = wrapper.get('[aria-label="Card assignments"]')
    await assignments.findAll('input[type="checkbox"]')[0]!.setValue(true)
    await fieldButton(wrapper, 'Save assignments')!.trigger('click')
    await nextTick()
    expect(cardsApi.replaceAssignments).toHaveBeenCalledWith('board-1', 'card-1', ['user-2'], 'v1')

    // The archive commits first. The parent card snapshot still says v1, but
    // the editor knows the card is archived and keeps the in-flight assignment
    // receipt owned until it settles.
    archive.resolve()
    await flushPromises()
    expect(wrapper.get('[data-testid="card-archive-kept-draft"]').text()).toContain('This card is now archived')
    const restoreWhileSaving = fieldButton(wrapper, 'Restore card')!
    expect((restoreWhileSaving.element as HTMLButtonElement).disabled).toBe(true)

    // Once the assignment receipt clears the local draft, the notice may leave
    // with it, but the editor must not offer a restore that would send the
    // pre-archive v1 from its stale prop and deterministically conflict.
    assignment.resolve(savedCard)
    await flushPromises()
    expect(wrapper.find('[data-testid="card-archive-kept-draft"]').exists()).toBe(false)
    const restoreAfterSettlement = fieldButton(wrapper, 'Restore card')!
    expect((restoreAfterSettlement.element as HTMLButtonElement).disabled).toBe(true)
    await restoreAfterSettlement.trigger('click')
    expect(mockStore.setCardArchived).toHaveBeenCalledTimes(1)

    wrapper.unmount()
  })

  /*
   * #3017. The same refusal must also be RELEASED. A background board refetch
   * that re-reports write permission mid-PUT makes the assignment field reload
   * its participants; that read used to cancel the save's own settlement, so
   * the editor kept reporting a save in flight and refused every close
   * affordance until it was remounted by navigation.
   */
  it('releases the close affordances when a board refetch interrupted the save', async () => {
    const store = reactive({ ...mockStore, currentBoard: { id: 'board-1', canWrite: true, isArchived: false } })
    vi.mocked(useBoardStore).mockReturnValue(store as never)
    const deferred = createDeferred<Card>()
    vi.mocked(cardsApi.replaceAssignments).mockReturnValue(deferred.promise)
    const wrapper = mount(CardModal, {
      props: { card, isOpen: true, labels, presentation: 'modal' },
      attachTo: document.body,
    })
    await flushPromises()

    const assignments = wrapper.get('[aria-label="Card assignments"]')
    await assignments.findAll('input[type="checkbox"]')[0]!.setValue(true)
    await fieldButton(wrapper, 'Save assignments')!.trigger('click')
    await nextTick()
    expect(cardsApi.replaceAssignments).toHaveBeenCalledWith('board-1', 'card-1', ['user-2'], 'v1')

    // The board refetch reports read-only and then writable again, all while
    // the PUT is unanswered.
    store.currentBoard.canWrite = false
    await flushPromises()
    store.currentBoard.canWrite = true
    await flushPromises()
    // The flip must really have reached the field and started a second read;
    // without this the test would pass even if the stub stopped propagating.
    expect(cardsApi.getParticipants).toHaveBeenCalledTimes(2)

    // Still refused: the mutation really is still in flight.
    await wrapper.get('[aria-label="Close card editor"]').trigger('click')
    await nextTick()
    expect(wrapper.emitted('close')).toBeUndefined()
    expect(savePendingDismissButton()).not.toBeNull()

    deferred.resolve(savedCard)
    await flushPromises()
    expect(savePendingDismissButton()).toBeNull()
    expect((store as unknown as Record<string, unknown>).currentBoardCards).toEqual([savedCard])

    // And the editor closes on the next request instead of trapping the user.
    await wrapper.get('[aria-label="Close card editor"]').trigger('click')
    await nextTick()
    expect(discardConfirmButton()).toBeNull()
    expect(wrapper.emitted('close')).toHaveLength(1)

    wrapper.unmount()
  })

  it('still drops a delayed receipt that belongs to a card the editor has left', async () => {
    const { wrapper, deferred } = await mountWithPendingAssignmentSave('inspector')
    const otherCard: Card = { ...card, id: 'card-2', title: 'Another card', updatedAt: 'other-v1' }

    await wrapper.setProps({ card: otherCard })
    await flushPromises()

    deferred.resolve(savedCard)
    await flushPromises()

    // The stale receipt neither commits to the board nor selects anyone on the
    // card now being edited.
    expect(mockStore.currentBoardCards).toEqual([card])
    const assignments = wrapper.get('[aria-label="Card assignments"]')
    expect((assignments.findAll('input[type="checkbox"]')[0]!.element as HTMLInputElement).checked).toBe(false)
    expect(savePendingDismissButton()).toBeNull()

    wrapper.unmount()
  })
})
