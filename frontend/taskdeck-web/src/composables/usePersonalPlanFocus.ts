import { ref } from 'vue'
import { useRouter } from 'vue-router'
import type { PlanCard } from '../api/workspacePlanApi'
import { useSessionStore } from '../store/sessionStore'
import { useWorkspacePlanStore } from '../store/workspacePlanStore'
import { getErrorDisplay } from './useErrorMapper'

/** Record only a destination the router accepted; a late save never navigates again. */
export function usePersonalPlanFocus() {
  const router = useRouter()
  const session = useSessionStore()
  const plan = useWorkspacePlanStore()
  const opening = ref(false)
  const navigationError = ref('')
  async function openFocus(card: PlanCard) {
    if (opening.value || !card.available || !session.userId || !plan.available || !plan.ready || plan.loading || plan.saving) return false
    const owner = session.userId
    const target = { path: `/workspace/boards/${card.boardId}/cards/${card.cardId}/thinking`, query: { focus: '1' } }
    opening.value = true
    navigationError.value = ''
    try {
      const failure = await router.push(target)
      if (failure || session.userId !== owner || router.currentRoute.value.fullPath !== router.resolve(target).fullPath) return false
      return await plan.focus(card.boardId, card.cardId)
    } catch (failure) {
      if (session.userId === owner) navigationError.value = getErrorDisplay(failure, 'This focus could not be opened. Try again.').message
      return false
    } finally { opening.value = false }
  }
  return { openFocus, opening, navigationError }
}
