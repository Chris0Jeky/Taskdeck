import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { reactive } from 'vue'
import CardModal from '../../components/board/CardModal.vue'
import CardArchiveAction from '../../components/board/CardArchiveAction.vue'
import { cardsApi } from '../../api/cardsApi'
import { boardsApi } from '../../api/boardsApi'
import { useBoardStore } from '../../store/boardStore'
import type { BoardDetail, Card } from '../../types/board'

vi.mock('../../api/cardsApi', () => ({
  cardsApi: {
    getCards: vi.fn(),
    getParticipants: vi.fn(),
    getCard: vi.fn(),
    previewDetach: vi.fn(),
    replaceAssignments: vi.fn(),
  },
}))
vi.mock('../../api/boardsApi', () => ({ boardsApi: { getBoard: vi.fn() } }))
vi.mock('../../store/boardStore', () => ({ useBoardStore: vi.fn() }))
vi.mock('../../store/sessionStore', () => ({ useSessionStore: () => ({ userId: 'user-1' }) }))
vi.mock('vue-router', async importOriginal => ({
  ...await importOriginal<typeof import('vue-router')>(),
  useRouter: () => ({ push: vi.fn() }),
}))

const originalUpdatedAt = '2026-09-12T10:00:00Z'
const committedUpdatedAt = '2026-09-12T11:00:00Z'
const nextUpdatedAt = '2026-09-12T12:00:00Z'

const archivedCard: Card = {
  id: 'card-a',
  boardId: 'board-1',
  columnId: 'column-1',
  title: 'Card A',
  description: '',
  labels: [],
  assignments: [],
  isBlocked: false,
  isArchived: true,
  blockReason: null,
  dueDate: null,
  position: 0,
  createdAt: '2026-09-12T09:00:00Z',
  updatedAt: originalUpdatedAt,
}

const otherCard: Card = {
  ...archivedCard,
  id: 'card-b',
  title: 'Card B',
  updatedAt: '2026-09-12T10:30:00Z',
}

const restoredCard: Card = {
  ...archivedCard,
  isArchived: false,
  updatedAt: committedUpdatedAt,
}

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((yes, no) => {
    resolve = yes
    reject = no
  })
  return { promise, resolve, reject }
}

function buttonByText(wrapper: VueWrapper, text: string) {
  const button = wrapper.findAll('button').find(candidate => candidate.text().trim() === text)
  if (!button) throw new Error(`Could not find button: ${text}`)
  return button
}

describe('CardModal late restore write baselines', () => {
  let store: ReturnType<typeof useBoardStore>
  let wrapper: VueWrapper | null = null

  beforeEach(() => {
    vi.resetAllMocks()
    vi.mocked(cardsApi.getCards).mockResolvedValue([])
    vi.mocked(cardsApi.getParticipants).mockResolvedValue([
      { userId: 'member-1', displayName: 'Member One' },
    ])
    vi.mocked(cardsApi.getCard).mockResolvedValue(restoredCard)
    vi.mocked(cardsApi.previewDetach).mockResolvedValue({
      cardId: archivedCard.id,
      expectedUpdatedAt: originalUpdatedAt,
      expectedChildrenFingerprint: 'v1:children',
      children: [],
    })

    store = reactive({
      currentBoard: {
        id: archivedCard.boardId,
        name: 'Board',
        description: null,
        isArchived: false,
        canWrite: true,
        createdAt: '2026-09-12T08:00:00Z',
        updatedAt: '2026-09-12T08:00:00Z',
        columns: [],
      } as BoardDetail,
      currentBoardCards: [archivedCard, otherCard],
      fetchCardComments: vi.fn().mockResolvedValue([]),
      fetchCardProvenance: vi.fn().mockResolvedValue(null),
      getCardComments: vi.fn().mockReturnValue([]),
      setEditingCard: vi.fn(),
      fetchBoard: vi.fn(),
      setCardArchived: vi.fn(),
      updateCard: vi.fn(),
      deleteCard: vi.fn(),
      createCardComment: vi.fn(),
    }) as unknown as ReturnType<typeof useBoardStore>
    vi.mocked(useBoardStore).mockReturnValue(store)
  })

  afterEach(() => {
    wrapper?.unmount()
    wrapper = null
    document.body.innerHTML = ''
  })

  async function mountAndSettleLateRestore() {
    const restore = deferred<Card>()
    vi.mocked(store.setCardArchived).mockReturnValueOnce(restore.promise)

    const view = mount(CardModal, {
      props: {
        card: archivedCard,
        isOpen: true,
        labels: [],
        presentation: 'inspector',
      },
      attachTo: document.body,
    })
    wrapper = view
    await flushPromises()

    await view.getComponent(CardArchiveAction).get('button').trigger('click')
    await flushPromises()
    expect(store.setCardArchived).toHaveBeenCalledWith(
      archivedCard.boardId,
      archivedCard.id,
      false,
      originalUpdatedAt,
      undefined,
    )

    await view.setProps({ card: otherCard })
    await flushPromises()
    await view.setProps({ card: archivedCard })
    await flushPromises()

    return { view, restore }
  }

  it('uses the committed restore version for a newer retained form draft', async () => {
    const { view, restore } = await mountAndSettleLateRestore()
    await view.get('#card-title').setValue('Retained local draft')

    vi.mocked(store.updateCard).mockResolvedValueOnce({
      ...restoredCard,
      title: 'Retained local draft',
      updatedAt: nextUpdatedAt,
    })
    restore.resolve(restoredCard)
    await flushPromises()

    expect((view.get('#card-title').element as HTMLInputElement).value).toBe('Retained local draft')
    const save = buttonByText(view, 'Save Changes')
    expect(save.attributes('disabled')).toBeUndefined()
    await save.trigger('click')
    await flushPromises()

    expect(store.updateCard).toHaveBeenCalledWith(
      archivedCard.boardId,
      archivedCard.id,
      expect.objectContaining({
        title: 'Retained local draft',
        expectedUpdatedAt: committedUpdatedAt,
      }),
    )
  })

  it('unlocks assignments and uses the committed restore version without replacing their draft', async () => {
    const { view, restore } = await mountAndSettleLateRestore()
    vi.mocked(cardsApi.replaceAssignments).mockResolvedValueOnce({
      ...restoredCard,
      updatedAt: nextUpdatedAt,
      assignments: [{
        userId: 'member-1',
        displayName: 'Member One',
        assignedAt: '2026-09-12T11:30:00Z',
        assignedByUserId: 'user-1',
      }],
    })

    restore.resolve(restoredCard)
    await flushPromises()

    const member = view.get('input[type="checkbox"][value="member-1"]')
    expect((member.element as HTMLInputElement).disabled).toBe(false)
    await member.setValue(true)

    const saveAssignments = buttonByText(view, 'Save assignments')
    expect(saveAssignments.attributes('disabled')).toBeUndefined()
    await saveAssignments.trigger('click')
    await flushPromises()

    expect(cardsApi.replaceAssignments).toHaveBeenCalledWith(
      archivedCard.boardId,
      archivedCard.id,
      ['member-1'],
      committedUpdatedAt,
    )
  })

  it('does not apply another card\'s late receipt to the selected editor', async () => {
    const { view, restore } = await mountAndSettleLateRestore()
    await view.setProps({ card: otherCard })
    await flushPromises()

    restore.resolve(restoredCard)
    await flushPromises()

    expect(view.getComponent(CardArchiveAction).props('card').id).toBe(otherCard.id)
    expect(view.getComponent(CardArchiveAction).props('card').updatedAt).toBe(otherCard.updatedAt)
    expect(store.updateCard).not.toHaveBeenCalled()
    expect(cardsApi.replaceAssignments).not.toHaveBeenCalled()
    expect(boardsApi.getBoard).not.toHaveBeenCalled()
  })
})
