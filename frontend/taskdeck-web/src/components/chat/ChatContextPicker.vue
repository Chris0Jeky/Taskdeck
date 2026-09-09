<script setup lang="ts">
import { computed, onScopeDispose, ref, watch } from 'vue'
import { cardsApi } from '../../api/cardsApi'
import { workspaceInsightsApi } from '../../api/workspaceInsights'
import { useSessionStore } from '../../store/sessionStore'
import { isDemoMode } from '../../utils/demoMode'
import type { Card } from '../../types/board'
import type { Memory } from '../../types/workspaceInsights'
import type { ChatAssetReference, ChatContextSelection } from '../../types/chat'
import ChatOriginalSourcePicker from './ChatOriginalSourcePicker.vue'

const props = defineProps<{ boardId: string | null; disabled?: boolean; suggestedCardId?: string }>()
const emit = defineEmits<{ change: [selection: ChatContextSelection | null] }>()
const session = useSessionStore()
const available = computed(() => !!props.boardId && !isDemoMode && !session.isDemo && !!session.userId)
const expanded = ref(false)
const loading = ref(false)
const ready = ref(false)
const error = ref('')
const cards = ref<Card[]>([])
const memories = ref<Memory[]>([])
const cardId = ref('')
const includeThinking = ref(false)
const selectedMemories = ref<string[]>([])
const selectedAssets = ref<ChatAssetReference[]>([])
const privateCount = computed(() => selectedMemories.value.length + selectedAssets.value.length)
let generation = 0

function clear() {
  generation++
  cards.value = []; memories.value = []; cardId.value = ''; includeThinking.value = false
  selectedMemories.value = []; selectedAssets.value = []; ready.value = false; loading.value = false; error.value = ''
  emit('change', null)
}
watch(() => [props.boardId, session.userId, session.token, available.value], () => {
  clear(); expanded.value = false
}, { flush: 'sync' })
watch([cardId, includeThinking, selectedMemories, selectedAssets], () => {
  if (!cardId.value) includeThinking.value = false
  const selected = memories.value.filter(memory => selectedMemories.value.includes(memory.id))
  emit('change', ready.value && (cardId.value || selected.length || selectedAssets.value.length) ? {
    cardId: cardId.value || null, includeThinking: includeThinking.value,
    memories: selected.map(memory => ({ id: memory.id, revision: memory.revision })),
    ...(selectedAssets.value.length ? { assets: selectedAssets.value } : {}),
  } : null)
}, { deep: true, flush: 'sync' })
function selectAssets(memoryId: string, selection: ChatAssetReference[]) {
  selectedAssets.value = [...selectedAssets.value.filter(asset => asset.memoryId !== memoryId), ...selection]
}

async function refresh() {
  clear()
  if (!available.value || props.disabled) return
  const request = generation
  loading.value = true
  try {
    const [boardCards, privateMemories] = await Promise.all([
      cardsApi.getCards(props.boardId!), workspaceInsightsApi.getMemories(props.boardId!),
    ])
    if (request !== generation) return
    cards.value = boardCards.filter(card => card.boardId === props.boardId)
    memories.value = privateMemories.filter(memory => memory.boardId === props.boardId && !memory.archived)
    ready.value = true
  } catch {
    if (request === generation) error.value = 'Sources could not be loaded. Refresh to check access and current versions.'
  } finally { if (request === generation) loading.value = false }
}
function toggle() {
  expanded.value = !expanded.value
  if (expanded.value && !ready.value && !loading.value) void refresh()
}
onScopeDispose(clear)
</script>

<template>
  <section v-if="available" class="context-picker" aria-label="Choose context for your next message">
    <button type="button" :aria-expanded="expanded" :disabled="disabled" @click="toggle">
      {{ expanded ? 'Hide source choices' : 'Choose sources' }}
      <span v-if="cardId || privateCount"> · {{ (cardId ? 1 : 0) + (includeThinking ? 1 : 0) + privateCount }} selected</span>
    </button>
    <div v-if="expanded">
      <p>Include a card, its shared thinking, or up to five private memories and original sources in your next message. Selected material is sent to your configured model. Board changes still go through Review.</p>
      <p v-if="loading" role="status">Checking sources…</p>
      <p v-if="error" role="alert">{{ error }}</p>
      <fieldset v-if="ready" :disabled="disabled">
        <label>Card
          <select v-model="cardId" aria-label="Context card">
            <option value="">No card selected</option>
            <option v-for="card in cards" :key="card.id" :value="card.id">{{ card.title }}</option>
          </select>
        </label>
        <button v-if="suggestedCardId && cards.some(card => card.id === suggestedCardId) && cardId !== suggestedCardId" type="button" @click="cardId = suggestedCardId!">Include the card you are working on</button>
        <label><input v-model="includeThinking" type="checkbox" :disabled="!cardId" /> Include shared thinking for this card</label>
        <p>Private memories and originals ({{ privateCount }}/5)</p>
        <p v-if="!memories.length">No saved memories for this board.</p>
        <div v-for="memory in memories" :key="memory.id">
        <label class="context-picker__memory">
          <input v-model="selectedMemories" type="checkbox" :value="memory.id" :disabled="privateCount >= 5 && !selectedMemories.includes(memory.id)" />
          <span>{{ memory.title }} · {{ memory.status }} · version {{ memory.revision }}<small>{{ memory.text }}</small></span>
        </label>
        <ChatOriginalSourcePicker :key="`${memory.id}:${memory.revision}`" :memory-id="memory.id" :board-id="boardId!"
          :revision="memory.revision" :disabled="disabled" :selected="selectedAssets.filter(asset => asset.memoryId === memory.id)"
          :selected-count="privateCount" @change="selectAssets(memory.id, $event)" />
        </div>
      </fieldset>
      <button type="button" :disabled="disabled || loading" @click="refresh">Refresh sources and clear selection</button>
      <p v-if="ready">Long sources may be excerpted. The saved turn lists exactly which sources were included.</p>
    </div>
  </section>
</template>

<style scoped>
.context-picker { margin-block: 1rem; padding: .75rem; border: 1px solid var(--td-border-default); border-radius: .5rem; color: var(--td-text-primary); background: var(--td-surface-secondary); }
button, select { color: inherit; background: var(--td-surface-primary); border: 1px solid var(--td-border-default); border-radius: .3rem; padding: .5rem; max-width: 100%; }
fieldset { border: 0; margin: 0; padding: 0; display: grid; gap: .75rem; }
select { display: block; width: 100%; }
p, small { font-size: .85rem; line-height: 1.5; }
.context-picker__memory { display: flex; gap: .5rem; align-items: start; overflow-wrap: anywhere; }
small { display: block; white-space: pre-wrap; max-height: 8rem; overflow: auto; }
</style>
