import { onUnmounted, ref, watch } from 'vue'
import { boardsApi } from '../api/boardsApi'
import { cardsApi } from '../api/cardsApi'
import { useSessionStore } from '../store/sessionStore'
import { getErrorDisplay } from './useErrorMapper'
import type { Board, Card } from '../types/board'

export function usePlanCardPicker() {
  const session = useSessionStore()
  const boards = ref<Board[]>([])
  const cards = ref<Card[]>([])
  const boardId = ref('')
  const cardId = ref('')
  const loadingBoards = ref(false)
  const loadingCards = ref(false)
  const error = ref<string | null>(null)
  let generation = 0
  let boardsGeneration = 0

  async function loadBoards() {
    if (!session.userId) return
    const current = ++boardsGeneration
    loadingBoards.value = true
    error.value = null
    boards.value = []
    try {
      const result: Board[] = []
      let offset = 0
      while (true) {
        const page = await boardsApi.getBoardsPaginated(undefined, false, offset, 200)
        if (current !== boardsGeneration) return
        result.push(...page.items.filter(board => !board.isArchived))
        if (!page.hasMore || page.items.length === 0) break
        offset += page.items.length
      }
      boards.value = result
      if (boardId.value && !result.some(board => board.id === boardId.value)) boardId.value = ''
    } catch (failure) {
      if (current === boardsGeneration) {
        generation++
        cards.value = []
        cardId.value = ''
        error.value = getErrorDisplay(failure, 'Projects could not be loaded.').message
      }
    } finally { if (current === boardsGeneration) loadingBoards.value = false }
  }

  async function loadCards() {
    const current = ++generation
    const selected = boardId.value
    cards.value = []
    cardId.value = ''
    loadingCards.value = Boolean(selected)
    error.value = null
    if (!selected || !session.userId) return
    try {
      const result = await cardsApi.getCards(selected)
      if (current === generation) cards.value = result
    } catch (failure) {
      if (current === generation) error.value = getErrorDisplay(failure, 'Cards could not be loaded.').message
    } finally { if (current === generation) loadingCards.value = false }
  }
  watch(boardId, loadCards)
  watch(() => session.userId, () => {
    generation++
    boardsGeneration++
    boards.value = []; cards.value = []; boardId.value = ''; cardId.value = ''
    loadingBoards.value = loadingCards.value = false
    error.value = null
  }, { flush: 'sync' })
  onUnmounted(() => { generation++; boardsGeneration++ })
  return { boards, cards, boardId, cardId, loadingBoards, loadingCards, error, loadBoards, loadCards }
}
