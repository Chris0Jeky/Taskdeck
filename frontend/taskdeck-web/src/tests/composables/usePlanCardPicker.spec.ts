import { mount, flushPromises, enableAutoUnmount } from '@vue/test-utils'
import { defineComponent, nextTick, reactive } from 'vue'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { usePlanCardPicker } from '../../composables/usePlanCardPicker'
import { boardsApi } from '../../api/boardsApi'
import { BOARD_REQUEST_TIMEOUT_MS } from '../../api/http'
import { cardsApi } from '../../api/cardsApi'
import type { Board, Card } from '../../types/board'

const session = reactive({ userId: 'first' as string | null })
vi.mock('../../store/sessionStore', () => ({ useSessionStore: () => session }))
vi.mock('../../api/boardsApi', () => ({ boardsApi: { getBoardsPaginated: vi.fn() } }))
vi.mock('../../api/cardsApi', () => ({ cardsApi: { getCards: vi.fn() } }))
enableAutoUnmount(afterEach)
function setup() {
  let model!: ReturnType<typeof usePlanCardPicker>
  const wrapper = mount(defineComponent({ setup() { model = usePlanCardPicker(); return () => null } }))
  return { model, wrapper }
}
describe('personal plan card choices', () => {
  beforeEach(() => { vi.resetAllMocks(); session.userId = 'first' })
  it('includes later pages rather than silently omitting older projects', async () => {
    vi.mocked(boardsApi.getBoardsPaginated)
      .mockResolvedValueOnce({ items: [{ id: 'first', isArchived: false } as Board], hasMore: true, totalCount: 2, offset: 0, limit: 200 })
      .mockResolvedValueOnce({ items: [{ id: 'second', isArchived: false } as Board], hasMore: false, totalCount: 2, offset: 1, limit: 200 })
    const { model } = setup()
    await model.loadBoards()
    expect(model.boards.value.map(board => board.id)).toEqual(['first', 'second'])
  })
  it('ignores an older project response when the selected project changes', async () => {
    let settle!: (value: Card[]) => void
    vi.mocked(cardsApi.getCards).mockReturnValueOnce(new Promise(resolve => { settle = resolve }))
      .mockResolvedValueOnce([{ id: 'second-card' } as Card])
    const { model } = setup()
    model.boardId.value = 'first'; await nextTick()
    model.boardId.value = 'second'; await flushPromises()
    settle([{ id: 'first-card' } as Card]); await flushPromises()
    expect(model.cards.value.map(card => card.id)).toEqual(['second-card'])
    expect(model.loadingCards.value).toBe(false)
  })
  it('does not restore old-account card choices after signout', async () => {
    let settle!: (value: Card[]) => void
    vi.mocked(cardsApi.getCards).mockReturnValueOnce(new Promise(resolve => { settle = resolve }))
    const { model } = setup()
    model.boardId.value = 'first'; await nextTick()
    session.userId = null
    settle([{ id: 'private-card' } as Card]); await flushPromises()
    expect(model.cards.value).toEqual([])
    expect(model.boardId.value).toBe('')
    expect(model.loadingCards.value).toBe(false)
  })
  it('bounds every board page with a timeout and no retry', async () => {
    vi.mocked(boardsApi.getBoardsPaginated)
      .mockResolvedValueOnce({ items: [{ id: 'first', isArchived: false } as Board], hasMore: true, totalCount: 2, offset: 0, limit: 200 })
      .mockResolvedValueOnce({ items: [{ id: 'second', isArchived: false } as Board], hasMore: false, totalCount: 2, offset: 1, limit: 200 })
    const { model } = setup()
    await model.loadBoards()
    expect(boardsApi.getBoardsPaginated).toHaveBeenCalledTimes(2)
    expect(boardsApi.getBoardsPaginated).toHaveBeenNthCalledWith(1, undefined, false, 0, 200, { timeout: BOARD_REQUEST_TIMEOUT_MS, skipRetry: true })
    expect(boardsApi.getBoardsPaginated).toHaveBeenNthCalledWith(2, undefined, false, 1, 200, { timeout: BOARD_REQUEST_TIMEOUT_MS, skipRetry: true })
    expect(model.loadingBoards.value).toBe(false)
  })
  it('clears old cards on project discovery failure and supports retry', async () => {
    vi.mocked(boardsApi.getBoardsPaginated).mockRejectedValueOnce(new Error('offline'))
      .mockResolvedValueOnce({ items: [{ id: 'restored' } as Board], hasMore: false, totalCount: 1, offset: 0, limit: 200 })
    const { model } = setup()
    model.cards.value = [{ id: 'old' } as Card]
    await model.loadBoards()
    expect(model.cards.value).toEqual([])
    expect(model.error.value).toBeTruthy()
    expect(model.loadingBoards.value).toBe(false)
    await model.loadBoards()
    expect(model.error.value).toBeNull()
    expect(model.boards.value[0]?.id).toBe('restored')
    expect(model.loadingBoards.value).toBe(false)
  })
})
