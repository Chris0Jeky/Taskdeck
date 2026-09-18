<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import { thinkingApi } from '../../api/thinkingApi'
import ThinkingAudioAnswer from './ThinkingAudioAnswer.vue'
import type { Memory, MemoryStatus } from '../../types/workspaceInsights'

const props = defineProps<{ boardId: string; cardId: string; layerId: string; revision: number; sourceReady: boolean }>()
const emit = defineEmits<{ 'dirty-change': [dirty: boolean]; busy: [busy: boolean] }>()
const opened = ref(false)
const activated = ref(false)
const audioOpened = ref(false)
const audioDirty = ref(false)
const audioBusy = ref(false)
const loading = ref(false)
const saving = ref(false)
const loaded = ref(false)
const text = ref('')
const status = ref<MemoryStatus>('statement')
const memory = ref<Memory | null>(null)
const error = ref('')
let request = 0
const dirty = computed(() => text.value.length > 0 || audioDirty.value || audioBusy.value)
watch(dirty, value => emit('dirty-change', value), { immediate: true })
watch([saving, audioBusy], () => emit('busy', saving.value || audioBusy.value), { flush: 'sync' })
async function load() {
  if (!props.sourceReady) return
  const current = ++request
  loading.value = true; error.value = ''
  try {
    const saved = await thinkingApi.getAnswer(props.boardId, props.cardId, props.layerId)
    if (current === request) { memory.value = saved; loaded.value = true }
  }
  catch { if (current === request) { error.value = 'Could not load your private answer. Your draft is still here.'; loaded.value = false } }
  finally { if (current === request) loading.value = false }
}
async function open() { activated.value = true; opened.value = !opened.value; if (opened.value && !loaded.value) await load() }
async function save() {
  if (!props.sourceReady || !loaded.value || saving.value || !text.value.trim()) return
  const submittedText = text.value
  const submittedStatus = status.value
  saving.value = true; error.value = ''
  try {
    memory.value = await thinkingApi.answer(props.boardId, props.cardId, props.layerId, props.revision, submittedText, submittedStatus)
    if (text.value === submittedText && status.value === submittedStatus) text.value = ''
  } catch (cause) {
    error.value = (cause as { response?: { status?: number } }).response?.status === 409
      ? 'The saved question or your answer changed. Your draft is kept. Reload the saved answer, or open private memory to correct it.'
      : 'Could not keep your answer. Your draft is still here; check your connection and board access.'
  } finally { saving.value = false }
}
watch(() => [props.revision, props.sourceReady], () => {
  request++; loaded.value = false; loading.value = false
  if (opened.value && props.sourceReady) void load()
})
</script>

<template>
  <section class="private-answer" aria-label="Your private answer">
    <button type="button" :aria-expanded="opened" :disabled="saving || audioBusy" @click="open">{{ opened ? 'Hide private answer' : 'Your private answer' }}</button>
    <div v-if="activated" v-show="opened">
      <p>The question above is shared. Your answer is private memory; it will not edit the task or appear in board exports.</p>
      <p v-if="!sourceReady" role="status">Save your shared thinking before keeping a private answer.</p>
      <p v-if="loading" role="status">Loading your answer…</p>
      <div v-if="error" role="alert"><p>{{ error }}</p><button type="button" :disabled="!sourceReady || loading" @click="load">Reload saved answer</button></div>
      <div v-if="memory">
        <p><strong>{{ memory.archived ? 'Archived private answer' : 'Kept in your private memory' }}</strong> · {{ memory.status }}</p>
        <p class="answer-text">{{ memory.text }}</p>
        <RouterLink :to="{ path: '/workspace/memory', query: { boardId } }">Review or correct in private memory</RouterLink>
        <p v-if="text.length">You still have an unsaved answer draft:</p>
        <textarea v-if="text.length" v-model="text" aria-label="Unsaved private answer draft" rows="3" maxlength="8000" />
        <button v-if="text.length" type="button" @click="text = ''">Discard this private draft</button>
      </div>
      <div v-else>
        <label>Private answer<textarea v-model="text" aria-label="Private answer" rows="3" maxlength="8000" placeholder="What do you know, or what remains unclear?" /></label>
        <label>How to treat this answer<select v-model="status" aria-label="Private answer status"><option value="statement">Statement</option><option value="assumption">Assumption</option><option value="unknown">Unknown</option><option value="needsReview">Needs review</option></select></label>
        <p>Unknowns and answers needing review can appear in Quiet insights when you analyze this board.</p>
        <button type="button" :disabled="!sourceReady || !loaded || saving || audioBusy || !text.trim()" @click="save">{{ saving ? 'Keeping answer…' : 'Keep answer privately' }}</button>
      </div>
      <button v-if="!audioOpened" type="button" :disabled="saving" @click="audioOpened = true">Record or open a private audio answer</button>
      <ThinkingAudioAnswer v-if="audioOpened" :board-id="boardId" :card-id="cardId" :layer-id="layerId" :revision="revision" :source-ready="sourceReady && !saving" :answer-already-kept="!!memory" @dirty-change="audioDirty = $event" @busy="audioBusy = $event" @confirmed="load" />
    </div>
  </section>
</template>

<style scoped>
.private-answer { border-top: 1px dashed var(--td-border-default, #d7d1c6); margin-top: .8rem; padding-top: .8rem; font-size: .85rem; }
p { margin: .55rem 0; }
label { display: block; margin: .7rem 0; }
textarea { display: block; width: 100%; min-height: 5rem; padding: .5rem; background: transparent; border: 1px solid var(--td-border-default, #d7d1c6); border-radius: .4rem; }
select,button { padding: .4rem .6rem; background: var(--td-surface-container, #fffdf7); border: 1px solid var(--td-border-default, #d7d1c6); border-radius: .4rem; }
button:disabled { opacity: .5; }
.answer-text { white-space: pre-wrap; }
a { text-decoration: underline; }
:focus-visible { outline: 2px solid #677249; outline-offset: 2px; }
</style>
