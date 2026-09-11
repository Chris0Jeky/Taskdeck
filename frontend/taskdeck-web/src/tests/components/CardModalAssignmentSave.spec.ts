import { describe, it, expect, beforeEach, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { nextTick } from 'vue'
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
      expect(document.body.textContent).toContain('cannot be discarded')

      // The save the user was never allowed to "discard" commits, and the
      // editor shows the committed state instead of a false discard.
      deferred.resolve(savedCard)
      await flushPromises()
      expect(wrapper.emitted('close')).toBeUndefined()
      expect(document.body.textContent).not.toContain('cannot be discarded')

      wrapper.unmount()
    })
  }
})
