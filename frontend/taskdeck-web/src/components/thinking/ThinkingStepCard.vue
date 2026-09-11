<script setup lang="ts">
import { computed, onUnmounted, ref, watch } from 'vue'
import { boardsApi } from '../../api/boardsApi'
import { cardsApi } from '../../api/cardsApi'
import { thinkingApi } from '../../api/thinkingApi'
import { getErrorDisplay } from '../../composables/useErrorMapper'
import type { BoardDetail, Card } from '../../types/board'
import type { ThinkingDeck, ThinkingItem } from '../../types/thinking'

const props = defineProps<{ boardId: string; cardId: string; layerId: string; item: ThinkingItem; revision: number; sourceReady: boolean; canWrite: boolean }>()
const emit = defineEmits<{ promoted: [deck: ThinkingDeck]; busy: [value: boolean]; 'dirty-change': [value: boolean] }>()
const open = ref(false)
const loading = ref(false)
const busy = ref(false)
const error = ref('')
const board = ref<BoardDetail | null>(null)
const linkedCard = ref<Card | null>(null)
const title = ref('')
const columnId = ref('')
let generation = 0
const column = computed(() => board.value?.columns.find(value => value.id === linkedCard.value?.columnId))
watch(open, value => emit('dirty-change', value))
async function refresh() {
  const current = ++generation
  loading.value = true
  error.value = ''
  board.value = null
  linkedCard.value = null
  try {
    const [nextBoard, cards] = await Promise.all([boardsApi.getBoard(props.boardId), cardsApi.getCards(props.boardId)])
    if (current !== generation) return
    board.value = nextBoard
    linkedCard.value = cards.find(card => card.id === props.item.linkedCardId) ?? null
  } catch (cause) {
    if (current === generation) error.value = getErrorDisplay(cause, 'Could not load the linked work. Retry when connected.').message
  } finally { if (current === generation) loading.value = false }
}
async function start() {
  open.value = true
  title.value = props.item.text.slice(0, 200)
  columnId.value = ''
  await refresh()
}
async function create() {
  if (busy.value || !props.sourceReady || !props.canWrite || !columnId.value || !title.value.trim()) return
  busy.value = true
  emit('busy', true)
  error.value = ''
  try {
    const deck = await thinkingApi.promote(props.boardId, props.cardId, props.layerId, props.item.id, props.revision, columnId.value, title.value.trim())
    emit('promoted', deck)
    open.value = false
  } catch (cause) {
    error.value = getErrorDisplay(cause, 'Could not create the linked card. Your choices are retained.').message
  } finally { busy.value = false; emit('busy', false) }
}
watch(() => props.item.linkedCardId, id => { if (id) void refresh() }, { immediate: true })
onUnmounted(() => { generation++ })
</script>

<template>
  <div class="step-card">
    <p><strong>{{ item.text }}</strong></p>
    <template v-if="item.linkedCardId">
      <p v-if="loading" role="status">Loading linked card…</p>
      <p v-else-if="linkedCard">
        <RouterLink :to="`/workspace/boards/${boardId}/cards/${linkedCard.id}/thinking`">{{ linkedCard.title }}</RouterLink>
        <span> · {{ column?.name || 'Column unavailable' }}{{ linkedCard.isBlocked ? ' · Blocked' : '' }}</span>
      </p>
      <p v-else-if="!error">Linked card is no longer available. Removing this step will not delete any card.</p>
      <button type="button" :disabled="loading" @click="refresh">Refresh card status</button>
    </template>
    <template v-else-if="canWrite">
      <button v-if="!open" type="button" :disabled="!sourceReady" @click="start">Create card from step…</button>
      <div v-else role="group" aria-label="Create linked card" class="promotion">
        <p>Create one card on this board and keep it linked here. This is a manual board change.</p>
        <p v-if="loading" role="status">Loading destinations…</p>
        <label>Card title<input v-model="title" maxlength="200" :disabled="busy"></label>
        <label>Destination column<select v-model="columnId" aria-label="Destination column" :disabled="loading || busy"><option value="">Choose a column</option><option v-for="destination in board?.columns" :key="destination.id" :value="destination.id">{{ destination.name }}{{ destination.wipLimit ? ` (${destination.cardCount}/${destination.wipLimit})` : '' }}</option></select></label>
        <p v-if="board && !board.columns.length">Add a column on the board first.</p>
        <button type="button" :disabled="busy" @click="open = false">Cancel card creation</button>
        <button v-if="!board && !loading" type="button" @click="refresh">Retry destinations</button>
        <button type="button" :disabled="busy || loading || !board || board.isArchived || !sourceReady || !columnId || !title.trim()" @click="create">{{ busy ? 'Creating…' : 'Create linked card' }}</button>
      </div>
    </template>
    <p v-if="error" role="alert">{{ error }}</p>
  </div>
</template>

<style scoped>
.step-card{overflow-wrap:anywhere;font-size:.85rem;margin:.5rem 0 .8rem;padding-left:1.5rem}.step-card p{margin:.4rem 0}.step-card a{color:var(--td-text-primary);text-decoration:underline}.step-card button,.step-card input,.step-card select{font:inherit;color:inherit;background:var(--td-surface-container);border:1px solid var(--td-border-default);border-radius:.4rem;padding:.45rem;max-width:100%}.step-card button{margin-right:.4rem}.step-card button:disabled{opacity:.5}.promotion{padding:.75rem;border:1px solid var(--td-border-default);border-radius:.5rem}.promotion label{display:grid;gap:.3rem;margin:.6rem 0}.step-card :focus-visible{outline:2px solid var(--td-text-primary);outline-offset:2px}
</style>
