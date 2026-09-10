<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { useSessionStore } from '../../store/sessionStore'
import { useWorkspaceLayoutStore } from '../../store/workspaceLayoutStore'
import { useWorkspaceAttentionStore } from '../../store/workspaceAttentionStore'
import { workspaceAttentionApi, type AttentionReminder } from '../../api/workspaceAttentionApi'
import { isDemoMode } from '../../utils/demoMode'

const route = useRoute(); const session = useSessionStore(); const layout = useWorkspaceLayoutStore()
const attention = useWorkspaceAttentionStore()
const reminder = ref<AttentionReminder | null>(null)
const boardId = computed(() => route.name === 'workspace-board' && typeof route.params.id === 'string' ? route.params.id : '')
let lastInput = Date.now(); let lastPoll = 0; let generation = 0; let running = false; let live = true
let timer: ReturnType<typeof setInterval> | undefined
let observer: MutationObserver | undefined
function eligible() {
  const active = document.activeElement
  return live && !isDemoMode && session.isAuthenticated && !session.isDemo && !!boardId.value && layout.presentation !== 'zen'
    && route.query.focus !== '1' && document.visibilityState === 'visible' && document.hasFocus()
    && Date.now() - lastInput >= 60_000
    && !(active instanceof HTMLElement && (active.matches('input,textarea,select') || active.isContentEditable))
    && !document.querySelector('[role="dialog"],dialog[open],[aria-modal="true"]')
}
function invalidate() { generation++; reminder.value = null }
function input(event: Event) {
  if (event.type === 'input' || (event.target instanceof HTMLElement &&
    (event.target.matches('input,textarea,select') || event.target.isContentEditable))) {
    lastInput = Date.now(); invalidate()
  }
}
function focusChanged() { if (!eligible()) invalidate() }
watch(() => [boardId.value, session.userId, session.isAuthenticated, layout.presentation, route.query.focus], invalidate, { flush: 'sync' })
watch(() => attention.settings?.enabled, enabled => { if (!enabled) invalidate() })
async function tick() {
  if (!eligible() || running || Date.now() - lastPoll < 300_000) return
  const current = generation; const owner = session.userId; const board = boardId.value
  running = true; lastPoll = Date.now()
  try {
    await attention.load()
    if (!eligible() || current !== generation || owner !== session.userId || !attention.settings?.enabled) return
    if (reminder.value) return
    const next = await workspaceAttentionApi.claim(board)
    if (eligible() && current === generation && owner === session.userId && board === boardId.value
      && attention.settings?.enabled && next?.boardId === board) reminder.value = next
  } catch { /* A missed reminder stays quiet and conserves the server budget. */ }
  finally { running = false }
}
onMounted(() => {
  document.addEventListener('keydown', input, true); document.addEventListener('input', input, true)
  document.addEventListener('focusin', focusChanged); document.addEventListener('visibilitychange', focusChanged)
  window.addEventListener('blur', invalidate)
  observer = new MutationObserver(focusChanged)
  observer.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['open', 'aria-modal', 'role'] })
  timer = setInterval(() => { if (!eligible()) invalidate(); else void tick() }, 60_000)
})
onUnmounted(() => {
  live = false; invalidate(); clearInterval(timer)
  observer?.disconnect()
  document.removeEventListener('keydown', input, true); document.removeEventListener('input', input, true)
  document.removeEventListener('focusin', focusChanged); document.removeEventListener('visibilitychange', focusChanged)
  window.removeEventListener('blur', invalidate)
})
</script>

<template>
  <aside v-if="reminder" class="attention-reminder" aria-label="Saved question reminder">
    <RouterLink :to="{ name: 'workspace-insights', query: { boardId: reminder.boardId } }" @click="invalidate">A saved question is ready when you are.</RouterLink>
    <button type="button" aria-label="Dismiss this reminder" @click="invalidate">Dismiss</button>
  </aside>
</template>

<style scoped>
.attention-reminder { display: flex; flex-wrap: wrap; justify-content: space-between; gap: .75rem; padding: .75rem 1rem; background: var(--td-surface-container); border-bottom: 1px solid var(--td-border-default); color: var(--td-text-primary); }
a { text-decoration: underline; } button { color: inherit; background: transparent; border: 1px solid var(--td-border-default); border-radius: .4rem; padding: .3rem .6rem; }
</style>
