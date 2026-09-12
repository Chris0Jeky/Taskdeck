import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { reactive } from 'vue'
import CardModal from '../../components/board/CardModal.vue'
import CardArchiveAction from '../../components/board/CardArchiveAction.vue'
import CardAssignmentField from '../../components/board/CardAssignmentField.vue'
import { cardsApi } from '../../api/cardsApi'
import { boardsApi } from '../../api/boardsApi'
import { useBoardStore } from '../../store/boardStore'
import type { BoardDetail, Card } from '../../types/board'

vi.mock('../../api/cardsApi', () => ({ cardsApi: {
  getCards: vi.fn(), getParticipants: vi.fn(), getCard: vi.fn(), replaceAssignments: vi.fn(),
} }))
vi.mock('../../api/boardsApi', () => ({ boardsApi: { getBoard: vi.fn() } }))
vi.mock('../../store/boardStore', () => ({ useBoardStore: vi.fn() }))
vi.mock('../../store/sessionStore', () => ({ useSessionStore: () => ({ userId: 'user-1' }) }))

const card: Card = {
  id: 'card-1', boardId: 'board-1', columnId: 'column-1', title: 'Assignments',
  description: '', labels: [], isBlocked: false, blockReason: null, dueDate: null, position: 0,
  createdAt: '2026-09-10T00:00:00Z', updatedAt: 'v1', assignments: [],
}
const board = (canWrite: boolean | undefined) => ({
  id: card.boardId, canWrite, isArchived: false, columns: [],
}) as unknown as BoardDetail
function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (cause: unknown) => void
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}
function button(wrapper: ReturnType<typeof mount>, text: string) {
  return wrapper.findAll('button').find(candidate => candidate.text() === text)!
}

describe('CardModal permission reconciliation', () => {
  let store: ReturnType<typeof useBoardStore>
  beforeEach(() => {
    vi.resetAllMocks()
    vi.mocked(cardsApi.getCards).mockResolvedValue([])
    vi.mocked(cardsApi.getParticipants).mockResolvedValue([{ userId: 'user-2', displayName: 'Teammate' }])
    vi.mocked(cardsApi.getCard).mockResolvedValue(card)
    vi.mocked(boardsApi.getBoard).mockResolvedValue(board(true))
    store = reactive({
      currentBoard: board(true), currentBoardCards: [card],
      fetchCardComments: vi.fn().mockResolvedValue([]), fetchCardProvenance: vi.fn().mockResolvedValue(null),
      getCardComments: vi.fn().mockReturnValue([]), setEditingCard: vi.fn(), fetchBoard: vi.fn(),
    }) as unknown as ReturnType<typeof useBoardStore>
    vi.mocked(useBoardStore).mockReturnValue(store)
  })
  afterEach(() => { document.body.innerHTML = '' })

  async function pendingSave(presentation: 'modal' | 'inspector' = 'modal') {
    const save = deferred<Card>()
    vi.mocked(cardsApi.replaceAssignments).mockReturnValue(save.promise)
    const wrapper = mount(CardModal, { props: { card, isOpen: true, labels: [], presentation } })
    await flushPromises()
    await wrapper.get('#card-title').setValue('Kept title')
    await wrapper.get('[aria-label="Card assignments"] input').setValue(true)
    await button(wrapper, 'Save assignments').trigger('click')
    return { wrapper, save }
  }

  it.each([
    ['modal', true], ['inspector', true], ['modal', undefined], ['inspector', undefined],
  ] as const)('%s: reconciles cached canWrite=%s and restores a quiet board without losing drafts', async (presentation, canWrite) => {
    store.currentBoard = board(canWrite)
    const { wrapper, save } = await pendingSave(presentation)
    expect(boardsApi.getBoard).toHaveBeenCalledTimes(canWrite === undefined ? 1 : 0)
    vi.mocked(boardsApi.getBoard).mockClear()
    const permission = deferred<BoardDetail>()
    vi.mocked(boardsApi.getBoard).mockReturnValueOnce(permission.promise)
    save.reject({ response: { status: 403 } })
    await flushPromises()
    expect(boardsApi.getBoard).toHaveBeenCalledTimes(1)
    expect(wrapper.get('#card-parent').attributes('disabled')).toBeDefined()
    expect(wrapper.get('#card-work-item-type').attributes('disabled')).toBeDefined()
    expect(wrapper.findComponent(CardArchiveAction).props('canWrite')).toBe(false)
    expect(wrapper.findComponent(CardAssignmentField).props('readOnly')).toBe(true)
    expect(wrapper.get('#card-title').element.closest('fieldset[disabled]')).not.toBeNull()
    expect(button(wrapper, 'Save Changes').attributes('disabled')).toBeDefined()
    expect(button(wrapper, 'Delete Card').attributes('disabled')).toBeDefined()
    expect(button(wrapper, 'Clear').attributes('disabled')).toBeUndefined()
    expect(wrapper.get('[data-testid="card-permission-recovery"]').text()).toContain('Checking current board access')

    permission.resolve(board(false))
    await flushPromises()
    expect(wrapper.get('[data-testid="card-permission-recovery"]').text()).toContain('read-only for you')
    await button(wrapper, 'Refresh current assignments').trigger('click')
    await flushPromises()
    expect(wrapper.findComponent(CardAssignmentField).get('fieldset').attributes('disabled')).toBeDefined()
    expect(boardsApi.getBoard).toHaveBeenCalledTimes(1)

    await button(wrapper, 'Refresh board permission').trigger('click')
    await flushPromises()
    expect(boardsApi.getBoard).toHaveBeenCalledTimes(2)
    expect(wrapper.findComponent(CardAssignmentField).get('fieldset').attributes('disabled')).toBeUndefined()
    expect(wrapper.findComponent(CardArchiveAction).props('canWrite')).toBe(true)
    expect(wrapper.get('#card-parent').attributes('disabled')).toBeUndefined()
    expect(wrapper.get('#card-work-item-type').attributes('disabled')).toBeUndefined()
    expect(wrapper.get('#card-title').element.closest('fieldset[disabled]')).toBeNull()
    expect((wrapper.get('#card-title').element as HTMLInputElement).value).toBe('Kept title')
    expect((wrapper.get('[aria-label="Card assignments"] input').element as HTMLInputElement).checked).toBe(true)
    expect(wrapper.text()).not.toContain('This assignment save was refused')
    expect(wrapper.emitted('close')).toBeUndefined()
    wrapper.unmount()
  })

  it('reconciles a late PUT403 after readOnly already changed true then false', async () => {
    const { wrapper, save } = await pendingSave()
    store.currentBoard!.canWrite = false
    await flushPromises()
    store.currentBoard!.canWrite = true
    await flushPromises()
    vi.mocked(boardsApi.getBoard).mockResolvedValueOnce(board(false))
    save.reject({ response: { status: 403 } })
    await flushPromises()
    expect(boardsApi.getBoard).toHaveBeenCalledTimes(1)
    expect(wrapper.findComponent(CardAssignmentField).props('readOnly')).toBe(true)
    expect(wrapper.emitted('saving-change')?.at(-1)).toEqual([false])
    await button(wrapper, 'Refresh board permission').trigger('click')
    await flushPromises()
    expect(wrapper.findComponent(CardAssignmentField).get('fieldset').attributes('disabled')).toBeUndefined()
    expect((wrapper.get('[aria-label="Card assignments"] input').element as HTMLInputElement).checked).toBe(true)
    wrapper.unmount()
  })

  it.each([403, 404, 500, undefined])('keeps failed/missing permission %s recoverable and prevents assignment reads', async (status) => {
    const { wrapper, save } = await pendingSave()
    if (status === undefined) vi.mocked(boardsApi.getBoard).mockResolvedValueOnce(board(undefined))
    else vi.mocked(boardsApi.getBoard).mockRejectedValueOnce({ response: { status } })
    save.reject({ response: { status: 403 } })
    await flushPromises()
    const field = wrapper.findComponent(CardAssignmentField)
    expect(field.props('readOnly')).toBe(true)
    expect(field.props('readsBlocked')).toBe(true)
    expect(wrapper.text()).not.toContain('stay readable')
    expect(wrapper.text()).not.toContain('Your edit permission was revoked')
    expect(button(wrapper, 'Refresh current assignments').attributes('disabled')).toBeDefined()
    const reads = vi.mocked(cardsApi.getParticipants).mock.calls.length
    await (field.vm as unknown as { load: (refresh: boolean) => Promise<void> }).load(true)
    expect(cardsApi.getParticipants).toHaveBeenCalledTimes(reads)
    await button(wrapper, 'Cancel assignment changes').trigger('click')
    expect((wrapper.get('[aria-label="Card assignments"] input').element as HTMLInputElement).checked).toBe(false)
    await button(wrapper, 'Refresh board permission').trigger('click')
    await flushPromises()
    expect(field.props('readOnly')).toBe(false)
    wrapper.unmount()
  })
})
