import { ref, watch } from 'vue'
import { defineStore } from 'pinia'
import { useSessionStore } from './sessionStore'
import { workspaceAttentionApi, type AttentionSettings } from '../api/workspaceAttentionApi'
import { isDemoMode } from '../utils/demoMode'

export const useWorkspaceAttentionStore = defineStore('workspaceAttention', () => {
  const session = useSessionStore()
  const settings = ref<AttentionSettings | null>(null)
  const busy = ref(false); const error = ref(''); let generation = 0
  watch(() => [session.userId, session.isAuthenticated, session.isDemo], () => {
    generation++; settings.value = null; busy.value = false; error.value = ''
  }, { flush: 'sync' })
  async function request(enabled?: boolean) {
    if (isDemoMode || !session.userId || !session.isAuthenticated || session.isDemo || busy.value) return
    if (enabled !== undefined && !settings.value) return
    const owner = session.userId; const current = ++generation; busy.value = true; error.value = ''
    try {
      const next = enabled === undefined ? await workspaceAttentionApi.get()
        : await workspaceAttentionApi.save(settings.value!.revision, enabled)
      if (current === generation && owner === session.userId) settings.value = next
    } catch {
      if (current === generation && owner === session.userId) {
        settings.value = null
        error.value = 'Your reminder preference could not be confirmed. Reload it before continuing.'
      }
    } finally { if (current === generation) busy.value = false }
  }
  return { settings, busy, error, load: () => request(), save: (enabled: boolean) => request(enabled) }
})
