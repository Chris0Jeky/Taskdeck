import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { reactive } from 'vue'
import CardModal from '../../components/board/CardModal.vue'
import CardArchiveAction from '../../components/board/CardArchiveAction.vue'
import { cardsApi } from '../../api/cardsApi'
import { boardsApi } from '../../api/boardsApi'
import { useBoardStore } from '../../store/boardStore'
import type { BoardDetail, Card } from '../../types/board'

vi.mock('../../api/cardsApi', () => ({ cardsApi: {
  getCards: vi.fn(), getParticipants: vi.fn(), getCard: vi.fn(), previewDetach: vi.fn(),
} }))
vi.mock('../../api/boardsApi', () => ({ boardsApi: { getBoard: vi.fn() } }))
vi.mock('../../store/boardStore', () => ({ useBoardStore: vi.fn() }))
vi.mock('../../store/sessionStore', () => ({ useSessionStore: () => ({ userId: 'user-1' }) }))
vi.mock('vue-router', async importOriginal => ({
  ...await importOriginal<typeof import('vue-router')>(),
  useRouter: () => ({ push: vi.fn() }),
}))

const card: Card = {
  id: 'card-a', boardId: 'board-1', columnId: 'column-1', title: 'Card A',
  description: '', labels: [], isBlocked: false, blockReason: null, dueDate: null, position: 0,
  createdAt: '2026-09-10T00:00:00Z', updatedAt: '2026-09-10T01:00:00Z', assignments: [],
}
const otherCard: Card = {
  ...card, id: 'card-b', title: 'Card B', isArchived: true, updatedAt: '2026-09-10T02:00:00Z',
}
function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (cause: unknown) => void
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}
function confirmButton() {
  return Array.from(document.body.querySelectorAll<HTMLButtonElement>('button'))
    .find(button => button.textContent?.trim() === 'Confirm archive')
}

describe('CardModal archive request ownership across keyed children', () => {
  let store: ReturnType<typeof useBoardStore>
  let wrapper: VueWrapper | null = null
  beforeEach(() => {
    vi.resetAllMocks()
    vi.mocked(cardsApi.getCards).mockResolvedValue([])
    vi.mocked(cardsApi.getParticipants).mockResolvedValue([])
    vi.mocked(cardsApi.getCard).mockResolvedValue(card)
    vi.mocked(cardsApi.previewDetach).mockResolvedValue({
      cardId: card.id, expectedUpdatedAt: card.updatedAt, expectedChildrenFingerprint: 'v1:children', children: [],
    })
    store = reactive({
      currentBoard: { id: card.boardId, canWrite: true, isArchived: false, columns: [] } as unknown as BoardDetail,
      currentBoardCards: [card, otherCard],
      fetchCardComments: vi.fn().mockResolvedValue([]), fetchCardProvenance: vi.fn().mockResolvedValue(null),
      getCardComments: vi.fn().mockReturnValue([]), setEditingCard: vi.fn(), fetchBoard: vi.fn(), setCardArchived: vi.fn(),
      updateCard: vi.fn(), deleteCard: vi.fn(), createCardComment: vi.fn(),
    }) as unknown as ReturnType<typeof useBoardStore>
    vi.mocked(useBoardStore).mockReturnValue(store)
  })
  afterEach(() => {
    wrapper?.unmount()
    wrapper = null
    document.body.innerHTML = ''
  })

  it.each([
    { archive: true, outcome: 'success' }, { archive: true, outcome: 'failure' },
    { archive: false, outcome: 'success' }, { archive: false, outcome: 'failure' },
  ])('A-to-B-to-A survives version-key remounts (archive=$archive, $outcome)', async ({ archive, outcome }) => {
    const original = deferred<Card>()
    const other = deferred<Card>()
    vi.mocked(store.setCardArchived).mockReturnValueOnce(original.promise).mockReturnValueOnce(other.promise)
    const originalCard = { ...card, isArchived: !archive }
    const view = mount(CardModal, {
      props: { card: originalCard, isOpen: true, labels: [], presentation: 'inspector' }, attachTo: document.body,
    })
    wrapper = view
    await flushPromises()
    const firstAction = view.getComponent(CardArchiveAction)
    await firstAction.get('button').trigger('click')
    await flushPromises()
    if (archive) {
      confirmButton()!.click()
      await flushPromises()
      // Dismissal releases the dialog, not the already-submitted archive.
      confirmButton()!.closest('[role="dialog"]')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
      await flushPromises()
      expect(confirmButton()).toBeUndefined()
      expect(view.emitted('close')).toBeUndefined()
    }
    expect(store.setCardArchived).toHaveBeenCalledTimes(1)

    await view.setProps({ card: otherCard })
    await flushPromises()
    const otherAction = view.getComponent(CardArchiveAction)
    expect(otherAction.vm === firstAction.vm).toBe(false)
    expect(otherAction.get('button').attributes('disabled')).toBeUndefined()
    await otherAction.get('button').trigger('click')
    await flushPromises()
    expect(vi.mocked(store.setCardArchived).mock.calls.map(call => call.slice(0, 3))).toEqual([
      [card.boardId, card.id, archive], [otherCard.boardId, otherCard.id, false],
    ])

    await view.setProps({ card: originalCard })
    await flushPromises()
    const returnedAction = view.getComponent(CardArchiveAction)
    expect(returnedAction.vm === firstAction.vm).toBe(false)
    expect(returnedAction.vm === otherAction.vm).toBe(false)
    expect(returnedAction.get('button').attributes('disabled')).toBeDefined()
    const previewCount = vi.mocked(cardsApi.previewDetach).mock.calls.length
    await returnedAction.get('button').trigger('click')
    await flushPromises()
    expect(store.setCardArchived).toHaveBeenCalledTimes(2)
    expect(cardsApi.previewDetach).toHaveBeenCalledTimes(previewCount)
    expect(confirmButton()).toBeUndefined()

    const focusTarget = view.get('[aria-label="Close card editor"]').element as HTMLElement
    focusTarget.focus()
    if (outcome === 'success') original.resolve({ ...originalCard, isArchived: archive })
    else original.reject({ response: { status: 403 } })
    await flushPromises()
    expect(returnedAction.get('button').attributes('disabled')).toBeUndefined()
    expect(returnedAction.find('[role="alert"]').exists()).toBe(false)
    expect(returnedAction.emitted('changed')).toBeUndefined()
    expect(returnedAction.emitted('permission-denied')).toBeUndefined()
    expect(view.emitted('updated')).toBeUndefined()
    expect(view.emitted('close')).toBeUndefined()
    expect(boardsApi.getBoard).not.toHaveBeenCalled()
    expect(document.activeElement).toBe(focusTarget)

    await view.setProps({ card: otherCard })
    await flushPromises()
    expect(view.getComponent(CardArchiveAction).get('button').attributes('disabled')).toBeDefined()
    other.resolve({ ...otherCard, isArchived: false })
    await flushPromises()
    expect(view.getComponent(CardArchiveAction).get('button').attributes('disabled')).toBeUndefined()
    expect(store.setCardArchived).toHaveBeenCalledTimes(2)
    expect(view.emitted('updated')).toBeUndefined()
    expect(view.emitted('close')).toBeUndefined()
  })

  it('a new card version still clears obsolete confirmation and recovery presentation', async () => {
    vi.mocked(store.setCardArchived).mockRejectedValueOnce(new Error('Old archive conflict'))
    const view = mount(CardModal, { props: { card, isOpen: true, labels: [] }, attachTo: document.body })
    wrapper = view
    await flushPromises()
    const oldAction = view.getComponent(CardArchiveAction)
    await oldAction.get('button').trigger('click')
    await flushPromises()
    confirmButton()!.click()
    await flushPromises()
    expect(document.body.textContent).toContain('Old archive conflict')
    expect(confirmButton()).toBeDefined()

    await view.setProps({ card: { ...card, updatedAt: '2026-09-10T03:00:00Z' } })
    await flushPromises()
    const currentAction = view.getComponent(CardArchiveAction)
    expect(currentAction.vm === oldAction.vm).toBe(false)
    expect(confirmButton()).toBeUndefined()
    expect(document.body.textContent).not.toContain('Old archive conflict')
    expect(currentAction.get('button').attributes('disabled')).toBeUndefined()
    expect(store.setCardArchived).toHaveBeenCalledTimes(1)
    expect(view.emitted('close')).toBeUndefined()
  })
})
