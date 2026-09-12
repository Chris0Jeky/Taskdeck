import { onBeforeUnmount, ref, watch, type Ref } from 'vue'
import { estimateRollupsApi } from '../api/estimateRollupsApi'
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

  async function refresh() {
    if (!open.value || !session.userId) return
    const current = ++generation
    const id = boardId.value
    loading.value = true
    error.value = null
    // A prior result is never presented as current while a refresh is uncertain.
    rollup.value = null
    try {
      const result = await estimateRollupsApi.get(id)
      if (current !== generation) return
      if (result.boardId !== id) throw new Error('The estimates response belongs to another board.')
      rollup.value = result
      stale.value = false
    } catch (failure) {
      if (current === generation) error.value = getErrorDisplay(failure, 'Could not load estimates. Refresh to try again.').message
    } finally {
      if (current === generation) loading.value = false
    }
  }

  function toggle() {
    open.value = !open.value
    if (open.value) void refresh()
    else { generation++; loading.value = false }
  }

  watch([boardId, () => session.userId, () => session.token], () => {
    generation++
    rollup.value = null
    error.value = null
    loading.value = false
    stale.value = false
    open.value = false
  }, { flush: 'sync' })

  // Realtime and successful board mutations replace/update the existing board
  // state. Invalidate immediately, then require one explicit bounded refresh.
  watch(revision, () => {
    generation++
    loading.value = false
    if (open.value) stale.value = true
  }, { flush: 'sync' })
  onBeforeUnmount(() => { generation++ })

  return { open, loading, stale, error, rollup, refresh, toggle }
}
