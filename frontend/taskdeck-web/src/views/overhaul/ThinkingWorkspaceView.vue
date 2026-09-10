<script setup lang="ts">
import { computed, defineAsyncComponent, onUnmounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { boardsApi } from '../../api/boardsApi'
import { cardsApi } from '../../api/cardsApi'
import ThinkingDeckPanel from '../../components/thinking/ThinkingDeckPanel.vue'
import TdDialog from '../../components/ui/TdDialog.vue'
import { useUnsavedWorkspaceNavigation } from '../../composables/useUnsavedWorkspaceNavigation'
import { getErrorDisplay } from '../../composables/useErrorMapper'
import type { BoardDetail, Card } from '../../types/board'
import { useSessionStore } from '../../store/sessionStore'
import { useWorkspacePlanStore } from '../../store/workspacePlanStore'
import { isDemoMode } from '../../utils/demoMode'

const AutomationChatView = defineAsyncComponent(() => import('../AutomationChatView.vue'))
const session = useSessionStore()
const plan = useWorkspacePlanStore()
const companionOpened = ref(false)
const companionDirty = ref(false)
const companionSending = ref(false)
const answerBusy = ref(false)

const route = useRoute()
const boardId = computed(() => String(route.params.boardId ?? ''))
const cardId = computed(() => String(route.params.cardId ?? ''))
const focused = computed(() => route.query.focus === '1')
const board = ref<BoardDetail | null>(null)
const card = ref<Card | null>(null)
const dirty = ref(false)
const loading = ref(true)
const error = ref<string | null>(null)
let generation = 0
const { leaveRequested, decide } = useUnsavedWorkspaceNavigation(() => dirty.value || companionDirty.value || companionSending.value || answerBusy.value)
function leave() {
  if (!companionSending.value && !answerBusy.value) decide(true)
}

async function load() {
  const current = ++generation
  board.value = null
  card.value = null
  dirty.value = false
  companionOpened.value = false
  companionDirty.value = false
  companionSending.value = false
  answerBusy.value = false
  loading.value = true
  error.value = null
  try {
    const [nextBoard, cards] = await Promise.all([boardsApi.getBoard(boardId.value), cardsApi.getCards(boardId.value)])
    if (current !== generation) return
    const nextCard = cards.find(item => item.id === cardId.value)
    if (!nextCard) {
      error.value = 'This card is no longer available on this board.'
      return
    }
    board.value = nextBoard
    card.value = nextCard
  } catch (failure) {
    if (current === generation) error.value = getErrorDisplay(failure, 'The thinking workspace could not be loaded.').message
  } finally {
    if (current === generation) loading.value = false
  }
}
// A different actor must never inherit private drafts or late receipts. Same-user token refresh keeps them.
watch([boardId, cardId, () => session.userId], load, { immediate: true, flush: 'sync' })
onUnmounted(() => { generation++ })
</script>

<template>
  <div class="thinking-workspace">
    <p v-if="focused && plan.error" role="alert">{{ plan.error }} <RouterLink to="/workspace/plan">Refresh personal plan</RouterLink></p>
    <nav aria-label="Card context"><RouterLink :to="`/workspace/boards/${boardId}`">← {{ board?.name || 'Back to board' }}</RouterLink><RouterLink v-if="!focused" :to="{ path: '/workspace/insights', query: { boardId } }">Quiet insights</RouterLink><RouterLink v-if="!focused" :to="{ path: '/workspace/memory', query: { boardId } }">Memory</RouterLink></nav>
    <p v-if="loading" role="status">Opening your thinking space…</p>
    <section v-else-if="error" role="alert"><p>{{ error }}</p><button type="button" @click="load">Try again</button></section>
    <template v-else-if="card">
      <p v-if="route.query.focus === '1'" role="status">FOCUS · One thread at a time. <RouterLink to="/workspace/plan">Return to your plan</RouterLink></p>
      <p v-if="focused">Before you leave, add a shared <strong>thread</strong> below for next time. Save it with the card’s thinking so it is here when you return.</p>
      <header><p class="thinking-workspace__eyebrow">ROOM TO THINK · {{ board?.name }}</p><h1>{{ card.title }}</h1><RouterLink to="/workspace/plan">Choose work for your personal plan</RouterLink><p>Keep possibilities, questions and next steps close to the work. A simple card can stay simple.</p></header>
      <ThinkingDeckPanel :key="card.id" :board-id="boardId" :card-id="cardId" @dirty-change="dirty = $event" @busy="answerBusy = $event" />
      <section v-if="!isDemoMode && !session.isDemo" aria-label="Card companion">
        <h2>Think it through with your companion</h2>
        <p>Keep this card nearby while you talk. Choose the sources for each turn, then preview proposed changes before opening Review.</p>
        <button v-if="!companionOpened" type="button" @click="companionOpened = true">Open card companion</button>
        <AutomationChatView v-if="companionOpened" :key="card.id" :board-id="boardId" :card-id="cardId" :thinking-dirty="dirty" embedded @dirty-change="companionDirty = $event" @sending-change="companionSending = $event" />
      </section>
    </template>
    <TdDialog :open="leaveRequested" :title="answerBusy ? 'Your private answer is still in progress' : companionSending ? 'A companion message is still sending' : 'Leave this thinking space?'" :description="answerBusy ? 'Stop the recording or wait for your save receipt before leaving. Closing the browser does not cancel a request already received by the server.' : companionSending ? 'Wait for the send to finish before leaving. Closing the browser does not cancel a message already sent to the server.' : dirty || companionDirty ? 'Your thinking or companion message has unsaved changes. Save or send it before leaving, or discard this draft.' : 'Your message has finished sending. You can leave this thinking space.'" @close="decide(false)"><template #footer><button type="button" @click="decide(false)">Keep editing</button><button type="button" :disabled="companionSending || answerBusy" @click="leave">{{ dirty || companionDirty ? 'Discard draft and leave' : 'Leave thinking space' }}</button></template></TdDialog>
  </div>
</template>

<style scoped>
.thinking-workspace > * { min-width: 0; overflow-wrap: anywhere; }
.thinking-workspace section > h2 { font: 450 24px/1.3 var(--serif, Georgia, serif); margin-bottom: .5rem; }
.thinking-workspace{max-width:1200px;margin:auto;display:grid;gap:28px;color:var(--ink,var(--td-text-primary));font-family:var(--sans,system-ui,sans-serif)}.thinking-workspace nav{display:flex;gap:24px;flex-wrap:wrap;font-size:13px}.thinking-workspace nav a{color:inherit;text-decoration:none}.thinking-workspace nav a:hover{text-decoration:underline}.thinking-workspace h1{font:450 clamp(28px,3vw,42px)/1.2 var(--serif,Georgia,serif);margin:0 0 14px}.thinking-workspace header>p{font-size:14px;line-height:1.6;color:var(--ink-muted,var(--td-text-secondary))}.thinking-workspace .thinking-workspace__eyebrow{font-size:11px;letter-spacing:.13em}.thinking-workspace button{padding:12px 18px;margin-right:10px;border:1px solid var(--line,var(--td-border-default));border-radius:8px;background:var(--paper-card,var(--td-surface-raised));color:inherit;cursor:pointer;font:inherit}
</style>
