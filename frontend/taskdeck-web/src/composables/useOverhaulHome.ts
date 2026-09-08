import { computed, onMounted, ref } from 'vue'
import { useWorkspaceStore } from '../store/workspaceStore'
import { useCaptureStore } from '../store/captureStore'
import { getErrorDisplay } from './useErrorMapper'

/** Shared real work projection. Presentation never writes to board state. */
export function useOverhaulHome() {
  const workspace = useWorkspaceStore()
  const captures = useCaptureStore()
  const captureText = ref('')
  const captureBusy = ref(false)
  const captureError = ref<string | null>(null)
  const captureSaved = ref(false)
  const boards = computed(() => workspace.homeSummary?.boards.recentBoards ?? [])
  const agenda = computed(() => {
    const today = workspace.todaySummary
    if (!today) return []
    const seen = new Set<string>()
    return [...today.blockedCards, ...today.dueTodayCards, ...today.overdueCards].filter(card => {
      if (seen.has(card.cardId)) return false
      seen.add(card.cardId)
      return true
    }).slice(0, 6)
  })

  async function refresh() {
    // Each store retains its own failed/degraded state; partial success is useful.
    await Promise.allSettled([workspace.fetchHomeSummary(), workspace.fetchTodaySummary()])
  }

  async function saveCapture() {
    if (!captureText.value.trim() || captureBusy.value) return
    const original = captureText.value
    captureBusy.value = true
    captureError.value = null
    captureSaved.value = false
    try {
      await captures.createItem({ text: original, boardId: null, source: 'Typed' })
      // A user may continue typing while the request is in flight.
      if (captureText.value === original) captureText.value = ''
      captureSaved.value = true
    } catch (error) {
      captureError.value = getErrorDisplay(error, 'Your thought could not be saved. Your text is still here.').message
    } finally {
      captureBusy.value = false
    }
  }

  onMounted(() => {
    if (!workspace.todaySummary) void workspace.fetchTodaySummary().catch(() => {
      // The workspace store exposes todayError to the view.
    })
  })

  return { workspace, boards, agenda, captureText, captureBusy, captureError, captureSaved, saveCapture, refresh }
}
