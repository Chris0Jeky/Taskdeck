import { onBeforeUnmount, ref, watch, type Ref } from 'vue'
import { estimateRollupsApi } from '../api/estimateRollupsApi'
import { BOARD_REQUEST_TIMEOUT_MS } from '../api/http'
import { useSessionStore } from '../store/sessionStore'
import { getErrorDisplay } from './useErrorMapper'
import type { BoardEstimateRollup } from '../types/estimateRollups'

export function useBoardEstimateRollups(boardId: Ref<string>, revision: Ref<unknown>) {
  const session = useSessionStore()
  const open = ref(false)
  const loading = ref(false)
  const stale = ref(false)
  const error = ref<string | null>(null)
  const rollup = ref<BoardEstimateRollup | null>(null)
  let generation = 0
  let controller: AbortController | null = null
  let deadline: ReturnType<typeof setTimeout> | null = null

  function clearDeadline() {
    if (deadline !== null) clearTimeout(deadline)
    deadline = null
  }

  function cancelPendingRead() {
    // Retire ownership before abort: an adapter may settle synchronously on cancellation.
    generation++
    clearDeadline()
    const pending = controller
    controller = null
    pending?.abort()
  }

  async function refresh() {
    if (!open.value || !session.userId) return
    cancelPendingRead()
    const current = generation
    const request = new AbortController()
    controller = request
    const id = boardId.value
    loading.value = true
    error.value = null
    // A prior result is never presented as current while a refresh is uncertain.
    rollup.value = null
    // Bound the UI even if a transport ignores abort or never settles its promise.
    deadline = setTimeout(() => {
      if (current !== generation) return
      cancelPendingRead()
      loading.value = false
      error.value = 'Loading estimates timed out. Refresh estimates to try again.'
    }, BOARD_REQUEST_TIMEOUT_MS)
    try {
      const result = await estimateRollupsApi.get(id, { signal: request.signal })
      if (current !== generation) return
      if (result.boardId !== id) throw new Error('The estimates response belongs to another board.')
      rollup.value = result
      stale.value = false
    } catch (failure) {
      if (current === generation) error.value = getErrorDisplay(failure, 'Could not load estimates. Refresh to try again.').message
    } finally {
      if (current === generation) {
        clearDeadline()
        controller = null
        loading.value = false
      }
    }
  }

  function toggle() {
    open.value = !open.value
    if (open.value) void refresh()
    else { cancelPendingRead(); loading.value = false }
  }

  watch([boardId, () => session.userId, () => session.token], () => {
    cancelPendingRead()
    rollup.value = null
    error.value = null
    loading.value = false
    stale.value = false
    open.value = false
  }, { flush: 'sync' })

  // Realtime and successful board mutations replace/update the existing board
  // state. Invalidate immediately, then require one explicit bounded refresh.
  watch(revision, () => {
    cancelPendingRead()
    loading.value = false
    if (open.value) stale.value = true
  }, { flush: 'sync' })
  onBeforeUnmount(cancelPendingRead)

  return { open, loading, stale, error, rollup, refresh, toggle }
}
