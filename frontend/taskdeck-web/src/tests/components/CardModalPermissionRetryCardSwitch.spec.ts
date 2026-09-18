import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick, reactive } from 'vue'
import CardModal from '../../components/board/CardModal.vue'
import CardModalForm from '../../components/board/card-modal/CardModalForm.vue'
import { boardsApi } from '../../api/boardsApi'
import { useBoardStore } from '../../store/boardStore'
import { useSessionStore } from '../../store/sessionStore'
import type { Card, Label } from '../../types/board'

function createDeferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise
    reject = rejectPromise
  })
  return { promise, resolve, reject }
}

vi.mock('../../api/cardsApi', () => ({
  cardsApi: {
    getCards: vi.fn().mockResolvedValue([]),
    getParticipants: vi.fn().mockResolvedValue([]),
    previewDetach: vi.fn().mockResolvedValue({
      cardId: 'card-1',
      expectedUpdatedAt: '2026-09-01T00:00:00Z',
      expectedChildrenFingerprint: 'v1:fixed',
      children: [],
    }),
  },
}))

vi.mock('../../api/boardsApi', () => ({
  boardsApi: { getBoard: vi.fn() },
}))

vi.mock('../../store/boardStore', () => ({
  useBoardStore: vi.fn(),
}))

vi.mock('../../store/sessionStore', () => ({
  useSessionStore: vi.fn(),
}))

describe('CardModal permission retry focus ownership', () => {
  let mockStore: Record<string, unknown>
  let card: Card
  let labels: Label[]

  beforeEach(() => {
    document.body.innerHTML = ''
    setActivePinia(createPinia())

    card = {
      id: 'card-1',
      boardId: 'board-1',
      columnId: 'column-1',
      title: 'First card',
      description: '',
      position: 0,
      dueDate: null,
      isBlocked: false,
      blockReason: null,
      labels: [],
      createdAt: '2026-09-01T00:00:00Z',
      updatedAt: '2026-09-01T00:00:00Z',
    }
    labels = []

    mockStore = reactive({
      currentBoard: { id: 'board-1', isArchived: false },
      currentBoardCards: [],
      updateCard: vi.fn().mockResolvedValue(card),
      deleteCard: vi.fn().mockResolvedValue(undefined),
      fetchCardComments: vi.fn().mockResolvedValue([]),
      fetchCardProvenance: vi.fn().mockResolvedValue(null),
      getCardComments: vi.fn().mockReturnValue([]),
      createCardComment: vi.fn().mockResolvedValue(undefined),
      updateCardComment: vi.fn().mockResolvedValue(undefined),
      deleteCardComment: vi.fn().mockResolvedValue(undefined),
      setCardArchived: vi.fn().mockResolvedValue(undefined),
      fetchBoard: vi.fn().mockResolvedValue(undefined),
      editingCardId: null,
      setEditingCard: vi.fn(),
    })

    vi.mocked(boardsApi.getBoard).mockReset()
    vi.mocked(useBoardStore).mockReturnValue(mockStore as never)
    vi.mocked(useSessionStore).mockReturnValue({ userId: 'user-1' } as never)
  })

  afterEach(() => {
    document.body.innerHTML = ''
    vi.restoreAllMocks()
  })

  it('keeps the replacement card close control focused when the old retry settles', async () => {
    const retry = createDeferred<Record<string, unknown>>()
    vi.mocked(boardsApi.getBoard)
      .mockRejectedValueOnce(new Error('offline'))
      .mockReturnValueOnce(retry.promise as never)

    const wrapper = mount(CardModal, {
      attachTo: document.body,
      props: {
        card,
        isOpen: true,
        labels,
        presentation: 'inspector',
      },
    })

    await flushPromises()
    const refresh = wrapper.get('[data-testid="card-type-permission-refresh"]')
    ;(refresh.element as HTMLButtonElement).focus()
    expect(document.activeElement).toBe(refresh.element)

    await refresh.trigger('click')
    await nextTick()
    expect(boardsApi.getBoard).toHaveBeenCalledTimes(2)
    expect(wrapper.find('[data-testid="card-type-permission-refresh"]').exists()).toBe(false)
    expect(document.activeElement).toBe(document.body)

    ;(mockStore.currentBoard as { canWrite?: boolean }).canWrite = true
    const replacement: Card = {
      ...card,
      id: 'card-2',
      title: 'Second card',
      updatedAt: '2026-09-02T00:00:00Z',
    }
    await wrapper.setProps({ card: replacement })
    await nextTick()
    await flushPromises()
    await nextTick()

    const close = wrapper.get('[aria-label="Close card editor"]')
    expect(document.activeElement).toBe(close.element)

    retry.resolve({ id: card.boardId, canWrite: true, isArchived: false })
    await flushPromises()
    await nextTick()
    expect(document.activeElement).toBe(close.element)

    wrapper.unmount()
  })

  it('prefers the focused replacement retry over stale ownership from the previous card', async () => {
    const replacement: Card = {
      ...card,
      id: 'card-2',
      title: 'Second card',
      updatedAt: '2026-09-02T00:00:00Z',
    }
    const wrapper = mount(CardModalForm, {
      attachTo: document.body,
      props: {
        card,
        canEditType: false,
        typePermissionChecking: false,
        typePermissionUnknown: true,
        formattedDueDate: '',
        isOverdue: false,
        workItemType: 'Task',
        title: card.title,
        description: '',
        dueDate: '',
        estimateHours: '',
        estimateMinutes: '',
        isBlocked: false,
        blockReason: '',
      },
    })

    const firstRetry = wrapper.get<HTMLButtonElement>(
      '[data-testid="card-type-permission-refresh"]',
    )
    firstRetry.element.focus()
    expect(document.activeElement).toBe(firstRetry.element)

    // Card A owns this in-flight retry. Switching to B while `checking` remains
    // true must not let that stale ownership outrank B's later focused retry.
    await wrapper.setProps({ typePermissionChecking: true })
    await wrapper.setProps({ card: replacement })
    await wrapper.setProps({ typePermissionChecking: false, typePermissionUnknown: true })

    const replacementRetry = wrapper.get<HTMLButtonElement>(
      '[data-testid="card-type-permission-refresh"]',
    )
    replacementRetry.element.focus()
    expect(document.activeElement).toBe(replacementRetry.element)

    await wrapper.setProps({ typePermissionUnknown: false, canEditType: true })
    await nextTick()
    await flushPromises()

    expect(document.activeElement).toBe(
      wrapper.get<HTMLSelectElement>('#card-work-item-type').element,
    )
    wrapper.unmount()
  })
})
