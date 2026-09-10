import { defineStore } from 'pinia'
import { computed, ref, watch } from 'vue'
import { isDemoMode } from '../utils/demoMode'
import { workspacePlanApi, type PlanReference, type WorkspacePlan } from '../api/workspacePlanApi'
import { getErrorDisplay } from '../composables/useErrorMapper'
import { useSessionStore } from './sessionStore'

/** Private server state; never persist card content in browser storage. */
export const useWorkspacePlanStore = defineStore('workspacePlan', () => {
  const session = useSessionStore()
  const available = computed(() => !isDemoMode && !session.isDemo)
  const plan = ref<WorkspacePlan | null>(null)
  const loading = ref(false)
  const saving = ref(false)
  const ready = ref(false)
  const error = ref<string | null>(null)
  let generation = 0
  let loadingRequest: Promise<void> | null = null
  watch(() => session.userId, () => {
    generation++
    loadingRequest = null
    plan.value = null
    ready.value = false
    loading.value = saving.value = false
    error.value = null
  }, { flush: 'sync' })

  async function load() {
    if (!available.value || !session.userId || saving.value) return
    if (loadingRequest) return loadingRequest
    const request = loadFresh().finally(() => {
      if (loadingRequest === request) loadingRequest = null
    })
    loadingRequest = request
    return request
  }

  async function loadFresh() {
    const current = ++generation
    loading.value = true
    ready.value = false
    error.value = null
    // Hide formerly readable titles until permissions are revalidated.
    plan.value = null
    try {
      const result = await workspacePlanApi.get()
      if (current !== generation) return
      plan.value = result
      ready.value = true
    } catch (failure) {
      if (current === generation) error.value = getErrorDisplay(failure, 'Your plan could not be loaded. Try again.').message
    } finally { if (current === generation) loading.value = false }
  }

  async function mutate(operation: 'Plan change' | 'Focus', action: (revision: number) => Promise<WorkspacePlan>) {
    if (!available.value || !session.userId || !ready.value || !plan.value || loading.value || saving.value) return false
    const current = ++generation
    saving.value = true
    error.value = null
    try {
      const result = await action(plan.value.revision)
      if (current !== generation) return false
      plan.value = result
      return true
    } catch (failure) {
      if (current === generation) {
        // A response may be lost after commit. Reload before issuing another write.
        ready.value = false
        plan.value = null
        error.value = `${operation} could not be confirmed. Refresh your plan before trying again. ${getErrorDisplay(failure, 'The request failed.').message}`
      }
      return false
    } finally { if (current === generation) saving.value = false }
  }

  function save(entries: PlanReference[]) {
    const material = entries.map(({ boardId, cardId, plannedDate }) => ({ boardId, cardId, plannedDate }))
    return mutate('Plan change', revision => workspacePlanApi.save(revision, material))
  }
  function focus(boardId: string, cardId: string) {
    return mutate('Focus', revision => workspacePlanApi.focus(revision, boardId, cardId))
  }
  return { available, plan, loading, saving, ready, error, load, save, focus }
})
