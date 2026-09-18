import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { reactive } from 'vue'
import CardModal from '../../components/board/CardModal.vue'
import { cardsApi } from '../../api/cardsApi'
import { boardsApi } from '../../api/boardsApi'
import { useBoardStore } from '../../store/boardStore'
import type { BoardDetail, Card } from '../../types/board'
import type { CardComment } from '../../types/comments'

vi.mock('../../api/cardsApi', () => ({
  cardsApi: {
    getCards: vi.fn(),
    getParticipants: vi.fn(),
    getCard: vi.fn(),
    replaceAssignments: vi.fn(),
    previewDetach: vi.fn(),
  },
}))
vi.mock('../../api/boardsApi', () => ({ boardsApi: { getBoard: vi.fn() } }))
vi.mock('../../store/boardStore', () => ({ useBoardStore: vi.fn() }))
vi.mock('../../store/sessionStore', () => ({ useSessionStore: () => ({ userId: 'user-1' }) }))

const card: Card = {
  id: 'card-1',
  boardId: 'board-1',
  columnId: 'column-1',
  title: 'Permission recovery',
  description: '',
  labels: [],
  isBlocked: false,
  blockReason: null,
  dueDate: null,
  position: 0,
  createdAt: '2026-09-18T00:00:00Z',
  updatedAt: 'v1',
  assignments: [],
}

const comment: CardComment = {
  id: 'comment-1',
  boardId: card.boardId,
  cardId: card.id,
  parentCommentId: null,
  authorUserId: 'user-1',
  authorUsername: 'owner',
  content: 'Delete me',
  isDeleted: false,
  editedAt: null,
  mentions: [],
  createdAt: '2026-09-18T00:00:00Z',
  updatedAt: '2026-09-18T00:00:00Z',
}

function board(canWrite: boolean | undefined): BoardDetail {
  return {
    id: card.boardId,
    name: 'Board',
    description: null,
    ownerId: 'user-1',
    isArchived: false,
    canWrite,
    columns: [],
    createdAt: '2026-09-18T00:00:00Z',
    updatedAt: '2026-09-18T00:00:00Z',
  } as BoardDetail
}

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (cause: unknown) => void
  const promise = new Promise<T>((yes, no) => {
    resolve = yes
    reject = no
  })
  return { promise, resolve, reject }
}

function exactButton(text: string): HTMLButtonElement {
  const match = Array.from(document.body.querySelectorAll('button'))
    .find(candidate => candidate.textContent?.trim() === text)
  if (!(match instanceof HTMLButtonElement)) throw new Error(`Button not found: ${text}`)
  return match
}

describe('CardModal comment-delete permission recovery', () => {
  let store: ReturnType<typeof useBoardStore>

  beforeEach(() => {
    vi.resetAllMocks()
    vi.mocked(cardsApi.getCards).mockResolvedValue([])
    vi.mocked(cardsApi.getParticipants).mockResolvedValue([])
    vi.mocked(cardsApi.getCard).mockResolvedValue(card)
    vi.mocked(cardsApi.previewDetach).mockResolvedValue({
      cardId: card.id,
      expectedUpdatedAt: card.updatedAt,
      expectedChildrenFingerprint: 'v1:children',
      children: [],
    })

    store = reactive({
      currentBoard: board(true),
      currentBoardCards: [card],
      currentBoardRequestGeneration: 1,
      currentBoardPayloadGeneration: 1,
      editingCardId: null,
      fetchCardComments: vi.fn().mockResolvedValue([comment]),
      fetchCardProvenance: vi.fn().mockResolvedValue(null),
      getCardComments: vi.fn().mockReturnValue([comment]),
      setEditingCard: vi.fn(),
      fetchBoard: vi.fn().mockResolvedValue(true),
      setCardArchived: vi.fn(),
      updateCard: vi.fn(),
      deleteCard: vi.fn(),
      createCardComment: vi.fn(),
      updateCardComment: vi.fn(),
      deleteCardComment: vi.fn(),
    }) as unknown as ReturnType<typeof useBoardStore>
    vi.mocked(useBoardStore).mockReturnValue(store)
  })

  afterEach(() => {
    document.body.innerHTML = ''
  })

  it('keeps denied-delete recovery and a usable focus target inside the active dialog', async () => {
    const reconciliation = deferred<BoardDetail>()
    vi.mocked(store.deleteCardComment).mockRejectedValueOnce({ response: { status: 403 } })
    vi.mocked(boardsApi.getBoard).mockReturnValueOnce(reconciliation.promise)

    const wrapper = mount(CardModal, {
      props: { card, isOpen: true, labels: [] },
      attachTo: document.body,
    })
    await flushPromises()

    exactButton('Delete').click()
    await flushPromises()

    const confirm = document.querySelector<HTMLButtonElement>('[data-testid="card-comment-delete-confirm"]')!
    confirm.focus()
    confirm.click()
    await flushPromises()

    expect(store.deleteCardComment).toHaveBeenCalledWith(card.boardId, card.id, comment.id)
    expect(boardsApi.getBoard).toHaveBeenCalledTimes(1)
    expect(confirm.disabled).toBe(true)

    const nestedRecovery = document.querySelector<HTMLElement>(
      '[data-testid="card-comment-delete-permission-recovery"]',
    )
    expect(nestedRecovery).not.toBeNull()
    expect(nestedRecovery?.textContent).toContain('Checking current board access')
    expect(document.body.querySelectorAll('[data-testid="card-permission-recovery"]')).toHaveLength(0)

    const cancel = document.querySelector<HTMLButtonElement>('[data-testid="card-comment-delete-cancel"]')!
    expect(document.activeElement).toBe(cancel)

    reconciliation.reject({ response: { status: 500 } })
    await flushPromises()

    expect(nestedRecovery?.textContent).toContain('Could not confirm current board permission')
    const retry = document.querySelector<HTMLButtonElement>(
      '[data-testid="card-comment-delete-permission-refresh"]',
    )!
    expect(retry.disabled).toBe(false)
    expect(document.body.contains(cancel)).toBe(true)
    wrapper.unmount()
  })

  it('lets an in-dialog retry restore the original confirmation without losing its target', async () => {
    vi.mocked(store.deleteCardComment).mockRejectedValueOnce({ response: { status: 403 } })
    vi.mocked(boardsApi.getBoard)
      .mockRejectedValueOnce({ response: { status: 500 } })
      .mockResolvedValueOnce(board(true))

    const wrapper = mount(CardModal, {
      props: { card, isOpen: true, labels: [] },
      attachTo: document.body,
    })
    await flushPromises()

    exactButton('Delete').click()
    await flushPromises()
    document.querySelector<HTMLButtonElement>('[data-testid="card-comment-delete-confirm"]')!.click()
    await flushPromises()

    const retry = document.querySelector<HTMLButtonElement>(
      '[data-testid="card-comment-delete-permission-refresh"]',
    )!
    retry.click()
    await flushPromises()

    expect(boardsApi.getBoard).toHaveBeenCalledTimes(2)
    expect(document.body.textContent).toContain('Board write permission confirmed')
    expect(document.querySelector<HTMLButtonElement>('[data-testid="card-comment-delete-confirm"]')?.disabled)
      .toBe(false)
    expect(document.body.textContent).toContain('Delete this comment?')
    wrapper.unmount()
  })
})
