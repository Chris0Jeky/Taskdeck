<script setup lang="ts">
import { onBeforeUnmount, ref, watch } from 'vue'
import { useSessionStore } from '../../store/sessionStore'
import { getErrorDisplay } from '../../composables/useErrorMapper'
import { cardsApi } from '../../api/cardsApi'
import { workspaceInsightsApi, type ObservationSource } from '../../api/workspaceInsights'
import type { Card } from '../../types/board'
import PaperHLBtn from '../paper/PaperHLBtn.vue'

const props = defineProps<{ boardId: string; disabled: boolean }>()
const emit = defineEmits<{ busy: [value: boolean]; generated: [] }>()
const session = useSessionStore()
const cards = ref<Card[]>([])
const cardId = ref('')
const source = ref<ObservationSource | null>(null)
const loading = ref(false)
const loaded = ref(false)
const error = ref('')
const notice = ref('')
let generation = 0
function message(value: unknown) {
  const display = getErrorDisplay(value, 'Analysis could not be confirmed. Refresh insights and preview the evidence before trying again.')
  return display.message
}
function reset() {
  generation++
  cards.value = []; cardId.value = ''; source.value = null; loaded.value = false
  error.value = ''; notice.value = ''; loading.value = false; emit('busy', false)
}
watch(() => props.boardId, reset)
watch(() => session.userId, reset, { flush: 'sync' })
onBeforeUnmount(() => { generation++ })
watch(cardId, () => { source.value = null; error.value = ''; notice.value = '' })
async function loadCards() {
  if (loading.value || props.disabled || !props.boardId) return
  const request = ++generation
  loading.value = true; error.value = ''; source.value = null; emit('busy', true)
  try {
    const result = await cardsApi.getCards(props.boardId)
    if (request === generation) { cards.value = result; loaded.value = true; cardId.value = '' }
  } catch (value) { if (request === generation) { cards.value = []; loaded.value = false; error.value = message(value) } }
  finally { if (request === generation) { loading.value = false; emit('busy', false) } }
}
async function preview() {
  if (loading.value || props.disabled || !cardId.value) return
  const request = ++generation
  loading.value = true; error.value = ''; notice.value = ''; source.value = null; emit('busy', true)
  try {
    const result = await workspaceInsightsApi.observationSource(props.boardId, cardId.value)
    if (request === generation) source.value = result
  } catch (value) { if (request === generation) error.value = message(value) }
  finally { if (request === generation) { loading.value = false; emit('busy', false) } }
}
async function analyze() {
  if (loading.value || props.disabled || !source.value) return
  const request = ++generation
  loading.value = true; error.value = ''; notice.value = ''; emit('busy', true)
  try {
    const result = await workspaceInsightsApi.generateObservations(props.boardId, source.value)
    if (request === generation) {
      notice.value = result.length ? `${result.length} model questions saved for your review.` : 'No new questions were saved. Existing dismissals, snoozes and muted categories remain respected.'
      emit('generated')
    }
  } catch (value) { if (request === generation) error.value = message(value) }
  finally { if (request === generation) { source.value = null; loading.value = false; emit('busy', false) } }
}
</script>

<template>
  <section class="observation-panel" aria-labelledby="model-observations-title">
    <h2 id="model-observations-title">Questions from your evidence</h2>
    <p>Preview one card, then explicitly send that excerpt to your configured model. Up to three private questions can be saved. Model questions are suggestions to evaluate, not verified facts.</p>
    <p>Uses your shared chat budget. Private memory and audio are excluded. Nothing runs in the background or changes your board.</p>
    <PaperHLBtn variant="ghost" :disabled="disabled || loading || !boardId" @click="loadCards">{{ loaded ? 'Refresh card choices' : 'Choose a card' }}</PaperHLBtn>
    <p v-if="loaded && !cards.length" role="status">There are no cards to analyze on this board.</p>
    <label v-if="cards.length">Card for model analysis
      <select v-model="cardId" :disabled="disabled || loading"><option value="">Select a card</option><option v-for="card in cards" :key="card.id" :value="card.id">{{ card.title }}</option></select>
    </label>
    <PaperHLBtn v-if="cardId" variant="ghost" :disabled="disabled || loading" @click="preview">Preview current evidence</PaperHLBtn>
    <div v-if="source" class="observation-panel__source">
      <h3>Evidence to send</h3><pre>{{ source.text }}</pre>
      <p v-if="source.truncated">This is a shortened excerpt. Questions can only cite the text shown here.</p>
      <PaperHLBtn variant="ember" :disabled="disabled || loading" @click="analyze">Analyze this evidence with model</PaperHLBtn>
    </div>
    <p v-if="loading" role="status">Working with your selected evidence…</p>
    <p v-if="error" role="alert">{{ error }}</p>
    <p v-if="notice" role="status">{{ notice }}</p>
  </section>
</template>

<style scoped>
.observation-panel { margin: 1rem 0; padding: 1.25rem; border: 1px solid var(--td-border-default); border-radius: 12px; background: var(--td-surface-container); color: var(--td-text-primary); }
.observation-panel h2 { font-size: 1.1rem; font-weight: 650; }
.observation-panel p { margin: .6rem 0; max-width: 80ch; }
.observation-panel label { display: block; margin: .8rem 0; }
.observation-panel select { display: block; max-width: 100%; padding: .5rem; color: inherit; background: var(--td-surface-container); }
.observation-panel__source { margin-top: 1rem; }
.observation-panel pre { white-space: pre-wrap; overflow-wrap: anywhere; padding: .8rem 0; font: inherit; }
</style>
