import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { nextTick, reactive } from 'vue'
import { createI18n } from 'vue-i18n'
import CardModalForm from '../../components/board/card-modal/CardModalForm.vue'
import en from '../../locales/en'
import CardModal from '../../components/board/CardModal.vue'
import { cardsApi } from '../../api/cardsApi'
import { boardsApi } from '../../api/boardsApi'
import { useBoardStore } from '../../store/boardStore'
import type { BoardDetail, Card } from '../../types/board'

vi.mock('../../api/cardsApi', () => ({ cardsApi: {
  getCards: vi.fn(), getParticipants: vi.fn(), getCard: vi.fn(), replaceAssignments: vi.fn(), previewDetach: vi.fn(),
} }))
vi.mock('../../api/boardsApi', () => ({ boardsApi: { getBoard: vi.fn() } }))
vi.mock('../../store/boardStore', () => ({ useBoardStore: vi.fn() }))
vi.mock('../../store/sessionStore', () => ({ useSessionStore: () => ({ userId: 'user-1' }) }))

function mountForm() {
  const i18n = createI18n({
    legacy: false,
    locale: 'en',
    messages: { en },
  })

  return mount(CardModalForm, {
    attachTo: document.body,
    props: {
      card: { dueDate: null } as unknown as Card,
      canEditType: false,
      typePermissionChecking: false,
      typePermissionUnknown: true,
      formattedDueDate: '',
      isOverdue: false,
      workItemType: 'Task',
      title: 'Permission focus card',
      description: '',
      dueDate: '',
      estimateHours: '',
      estimateMinutes: '',
      isBlocked: false,
      blockReason: '',
    },
    global: {
      plugins: [i18n],
      stubs: {
        TdDateField: { template: '<input />' },
        CardEstimateField: { template: '<div />' },
      },
    },
  })
}

describe('CardModalForm permission recovery focus', () => {
  beforeEach(() => {
    document.body.innerHTML = ''
  })

  afterEach(() => {
    document.body.innerHTML = ''
  })

  it('moves focus into the enabled type selector and keeps the live region mounted after a successful retry', async () => {
    const wrapper = mountForm()
    const refresh = wrapper.get('[data-testid="card-type-permission-refresh"]')
    ;(refresh.element as HTMLButtonElement).focus()
    expect(document.activeElement).toBe(refresh.element)

    await wrapper.setProps({ typePermissionChecking: true, typePermissionUnknown: false })
    expect(wrapper.get('[role="status"]').text()).not.toBe('')

    await wrapper.setProps({ typePermissionChecking: false, canEditType: true })
    await nextTick()

    const selector = wrapper.get('#card-work-item-type')
    expect((selector.element as HTMLSelectElement).disabled).toBe(false)
    expect(document.activeElement).toBe(selector.element)
    expect(wrapper.get('[role="status"]').text()).toBe('')
    wrapper.unmount()
  })
})

const dialogCard: Card = {
  id: 'card-1', boardId: 'board-1', columnId: 'column-1', title: 'Permission focus card',
  description: '', labels: [], isBlocked: false, blockReason: null, dueDate: null, position: 0,
  createdAt: '2026-09-10T00:00:00Z', updatedAt: 'v1', assignments: [],
}
const dialogBoard = (canWrite: boolean | undefined) => ({
  id: dialogCard.boardId, canWrite, isArchived: false, columns: [],
}) as unknown as BoardDetail
function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (cause: unknown) => void
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}

describe('CardModal permission retry focus stays inside the dialog (GH-3030)', () => {
  let store: ReturnType<typeof useBoardStore>

  beforeEach(() => {
    document.body.innerHTML = ''
    vi.resetAllMocks()
    vi.mocked(cardsApi.getCards).mockResolvedValue([])
    vi.mocked(cardsApi.getParticipants).mockResolvedValue([])
    vi.mocked(cardsApi.getCard).mockResolvedValue(dialogCard)
    vi.mocked(cardsApi.previewDetach).mockResolvedValue({
      cardId: dialogCard.id, expectedUpdatedAt: dialogCard.updatedAt, expectedChildrenFingerprint: 'v1:children', children: [],
    })
    store = reactive({
      currentBoard: dialogBoard(undefined), currentBoardCards: [dialogCard],
      currentBoardRequestGeneration: 1, currentBoardPayloadGeneration: 1,
      fetchCardComments: vi.fn().mockResolvedValue([]), fetchCardProvenance: vi.fn().mockResolvedValue(null),
      getCardComments: vi.fn().mockReturnValue([]), setEditingCard: vi.fn(), fetchBoard: vi.fn(), setCardArchived: vi.fn(),
      updateCard: vi.fn(), deleteCard: vi.fn(), createCardComment: vi.fn(),
    }) as unknown as ReturnType<typeof useBoardStore>
    vi.mocked(useBoardStore).mockReturnValue(store)
  })

  afterEach(() => {
    document.body.innerHTML = ''
  })

  it('moves focus to the enabled type selector when the second read grants write access', async () => {
    const retry = deferred<BoardDetail>()
    vi.mocked(boardsApi.getBoard)
      .mockRejectedValueOnce({ response: { status: 500 } })
      .mockReturnValueOnce(retry.promise)

    const wrapper = mount(CardModal, {
      props: { card: dialogCard, isOpen: true, labels: [] },
      attachTo: document.body,
    })
    await flushPromises()

    const refresh = wrapper.get('[data-testid="card-type-permission-refresh"]')
    ;(refresh.element as HTMLButtonElement).focus()
    await refresh.trigger('click')
    await flushPromises()
    expect(wrapper.get('[data-testid="card-permission-recovery"]').text()).toContain('Checking current board access')

    retry.resolve(dialogBoard(true))
    await flushPromises()

    const dialog = wrapper.get('[role="dialog"]').element as HTMLElement
    const selector = wrapper.get('#card-work-item-type').element as HTMLSelectElement
    expect(selector.disabled).toBe(false)
    expect(document.activeElement).toBe(selector)
    expect(dialog.contains(document.activeElement)).toBe(true)
    wrapper.unmount()
  })
})
