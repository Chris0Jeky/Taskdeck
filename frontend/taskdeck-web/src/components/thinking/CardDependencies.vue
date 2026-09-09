<script setup lang="ts">
import { computed, onUnmounted, ref, watch } from 'vue'
import { boardDependenciesApi, type BoardDependencies, type CardDependency } from '../../api/boardDependenciesApi'
import { cardsApi } from '../../api/cardsApi'
import { boardsApi } from '../../api/boardsApi'
import { getErrorDisplay } from '../../composables/useErrorMapper'
import { useSessionStore } from '../../store/sessionStore'
import type { BoardDetail, Card } from '../../types/board'

const props = defineProps<{ boardId: string; cardId: string }>()
const emit = defineEmits<{ busy: [value: boolean] }>()
const session = useSessionStore()
const expanded = ref(false)
const loading = ref(false)
const saving = ref(false)
const graph = ref<BoardDependencies | null>(null)
const cards = ref<Card[]>([])
const board = ref<BoardDetail | null>(null)
const selected = ref('')
const error = ref('')
let generation = 0
const prerequisites = computed(() => cards.value.filter(card => graph.value?.edges.some(edge => edge.cardId === props.cardId && edge.dependsOnCardId === card.id)))
const dependents = computed(() => cards.value.filter(card => graph.value?.edges.some(edge => edge.dependsOnCardId === props.cardId && edge.cardId === card.id)))
const choices = computed(() => cards.value.filter(card => card.id !== props.cardId && !prerequisites.value.some(value => value.id === card.id)))
function columnName(card: Card) { return board.value?.columns.find(column => column.id === card.columnId)?.name ?? 'Column unavailable' }
function clear() { graph.value = null; cards.value = []; board.value = null; selected.value = '' }
async function load() {
  if (saving.value) return
  const current = ++generation
  loading.value = true; error.value = ''; clear()
  try {
    const [nextGraph, nextCards, nextBoard] = await Promise.all([
      boardDependenciesApi.get(props.boardId), cardsApi.getCards(props.boardId), boardsApi.getBoard(props.boardId),
    ])
    if (current !== generation) return
    graph.value = nextGraph; cards.value = nextCards; board.value = nextBoard
  } catch (cause) {
    if (current === generation) error.value = getErrorDisplay(cause, 'Could not load dependencies. Check board access and retry.').message
  } finally { if (current === generation) loading.value = false }
}
function toggle() { expanded.value = !expanded.value; if (expanded.value) void load() }
async function save(edges: CardDependency[]) {
  if (!graph.value?.canWrite || saving.value || loading.value) return
  const current = generation
  saving.value = true; emit('busy', true); error.value = ''
  try {
    const next = await boardDependenciesApi.save(props.boardId, graph.value.revision, edges)
    if (current === generation) { graph.value = next; selected.value = '' }
  } catch (cause) {
    if (current === generation) {
      clear()
      error.value = getErrorDisplay(cause, 'Could not save dependencies. Reload to check the saved relationships before trying again.').message
    }
  } finally { if (current === generation) { saving.value = false; emit('busy', false) } }
}
function add() { if (selected.value && graph.value) void save([...graph.value.edges, { cardId: props.cardId, dependsOnCardId: selected.value }]) }
function remove(id: string) { if (graph.value) void save(graph.value.edges.filter(edge => edge.cardId !== props.cardId || edge.dependsOnCardId !== id)) }
watch([() => props.boardId, () => props.cardId, () => session.userId], () => {
  generation++; clear(); expanded.value = false; error.value = ''; loading.value = false; saving.value = false; emit('busy', false)
}, { flush: 'sync' })
onUnmounted(() => { generation++ })
</script>

<template>
  <section class="dependencies" aria-label="Card dependencies">
    <button type="button" class="toggle" :aria-expanded="expanded" :disabled="saving" @click="toggle">{{ expanded ? 'Hide dependencies' : 'Explore dependencies' }}</button>
    <div v-if="expanded" class="body">
      <header><div><p class="eyebrow">How the work connects</p><h3>Dependencies</h3></div><button type="button" :disabled="loading || saving" @click="load">Refresh dependencies</button></header>
      <p class="hint">Choose prerequisites explicitly. These connections leave card status, deadlines and assignments unchanged.</p>
      <p v-if="loading" role="status">Loading connections…</p>
      <div v-if="error" role="alert"><p>{{ error }}</p><button type="button" :disabled="loading || saving" @click="load">Reload dependencies</button></div>
      <template v-if="graph">
        <p v-if="!graph.canWrite" class="hint">Read-only · Editing needs access to an active board.</p>
        <h4>This card depends on</h4>
        <p v-if="!prerequisites.length" class="hint">No prerequisites chosen.</p>
        <ul>
          <li v-for="card in prerequisites" :key="card.id">
            <div><RouterLink :to="`/workspace/boards/${boardId}/cards/${card.id}/thinking`">{{ card.title }}</RouterLink><small>{{ columnName(card) }}{{ card.isBlocked ? ` · Blocked: ${card.blockReason || 'Reason not provided'}` : '' }}</small></div>
            <button v-if="graph.canWrite" type="button" :disabled="saving" :aria-label="`Remove prerequisite ${card.title}`" @click="remove(card.id)">Remove link</button>
          </li>
        </ul>
        <form v-if="graph.canWrite" @submit.prevent="add">
          <label>Prerequisite card<select v-model="selected" aria-label="Prerequisite card" :disabled="saving || graph.edges.length >= 500"><option value="">Choose a card on this board</option><option v-for="card in choices" :key="card.id" :value="card.id">{{ card.title }}</option></select></label>
          <button type="submit" :disabled="!selected || saving || graph.edges.length >= 500">{{ saving ? 'Saving…' : 'Add prerequisite' }}</button>
        </form>
        <p v-if="graph.edges.length >= 500" class="hint">This board has reached its 500 dependency limit.</p>
        <h4>Cards that depend on this</h4>
        <p v-if="!dependents.length" class="hint">No cards depend on this one.</p>
        <ul><li v-for="card in dependents" :key="card.id"><div><RouterLink :to="`/workspace/boards/${boardId}/cards/${card.id}/thinking`">{{ card.title }}</RouterLink><small>{{ columnName(card) }}{{ card.isBlocked ? ' · Blocked' : '' }}</small></div></li></ul>
        <p class="hint">Removing a link keeps both cards. Open a dependent card to change its prerequisites.</p>
      </template>
    </div>
  </section>
</template>

<style scoped>
.dependencies{margin-top:1.5rem;border-top:1px solid var(--td-border-default,#d7d1c6);padding-top:1rem;color:var(--td-text-primary,#332f29)}
.body{padding:1rem 0}header{display:flex;align-items:center;justify-content:space-between;gap:1rem}h3,h4{margin:.4rem 0}.eyebrow{font-size:.7rem;letter-spacing:.1em;text-transform:uppercase;margin:0}.hint,small{color:var(--td-text-secondary,#686156);font-size:.85rem;line-height:1.5}ul{list-style:none;padding:0}li{display:flex;justify-content:space-between;gap:.7rem;align-items:center;padding:.65rem;border:1px solid var(--td-border-default,#d7d1c6);border-radius:.5rem;margin:.5rem 0}li div{min-width:0}a{color:inherit;overflow-wrap:anywhere}small{display:block}button,select{font:inherit;color:inherit;background:var(--td-surface-container,#fffdf7);border:1px solid var(--td-border-default,#d7d1c6);border-radius:.4rem;padding:.5rem .65rem}button{cursor:pointer}button:disabled{opacity:.5;cursor:default}form{display:flex;align-items:end;gap:.6rem;flex-wrap:wrap;margin:1rem 0}label{flex:1;min-width:0;font-size:.85rem}select{display:block;width:100%;margin-top:.4rem}button:focus-visible,select:focus-visible,a:focus-visible{outline:2px solid var(--td-accent-primary,#677249);outline-offset:3px}button.toggle{font-weight:600}@media(max-width:520px){header,li{align-items:flex-start;flex-wrap:wrap}form{display:grid}header button{font-size:.8rem}}
</style>
