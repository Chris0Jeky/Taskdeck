import { onMounted, onUnmounted, ref } from 'vue'
import { onBeforeRouteLeave, onBeforeRouteUpdate } from 'vue-router'

/** Reuses the in-app dialog for navigation; the browser owns tab-close confirmation. */
export function useUnsavedWorkspaceNavigation(isDirty: () => boolean) {
  const leaveRequested = ref(false)
  let settle: ((leave: boolean) => void) | null = null
  function decide(leave: boolean) {
    const resolve = settle
    settle = null
    leaveRequested.value = false
    resolve?.(leave)
  }
  function guard() {
    if (!isDirty()) return true
    if (settle) return false
    leaveRequested.value = true
    return new Promise<boolean>(resolve => { settle = resolve })
  }
  function beforeUnload(event: BeforeUnloadEvent) {
    if (!isDirty()) return
    event.preventDefault()
    event.returnValue = ''
  }
  onBeforeRouteLeave(guard)
  onBeforeRouteUpdate((to, from) => to.fullPath !== from.fullPath ? guard() : true)
  onMounted(() => window.addEventListener('beforeunload', beforeUnload))
  onUnmounted(() => {
    window.removeEventListener('beforeunload', beforeUnload)
    decide(false)
  })
  return { leaveRequested, decide }
}
