<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import { thinkingAudioApi, type ThinkingAudio } from '../../api/thinkingAudioApi'
import { createSourceUploadId } from '../../utils/sourceUploadId'
import type { MemoryStatus } from '../../types/workspaceInsights'
import AudioAnswerRecorder from './AudioAnswerRecorder.vue'

const props = defineProps<{ boardId: string; cardId: string; layerId: string; revision: number; sourceReady: boolean; answerAlreadyKept?: boolean }>()
const emit = defineEmits<{ 'dirty-change': [dirty: boolean]; busy: [busy: boolean]; confirmed: [] }>()
const saved = ref<ThinkingAudio | null>(null)
const file = ref<File | null>(null)
type DraftSource = { boardId: string; cardId: string; layerId: string; revision: number }
const sourceNow = (): DraftSource => ({ boardId: props.boardId, cardId: props.cardId, layerId: props.layerId, revision: props.revision })
let nextDraftSource: DraftSource | null = null
function beginDraft() { nextDraftSource = sourceNow() }
const draftSource = ref<DraftSource | null>(null)
const staleDraft = computed(() => !!file.value && !!draftSource.value && (
  draftSource.value.boardId !== props.boardId || draftSource.value.cardId !== props.cardId
  || draftSource.value.layerId !== props.layerId || draftSource.value.revision !== props.revision))
const recording = ref(false)
const pending = ref(false)
const loading = ref(false)
const loaded = ref(false)
const text = ref('')
const writtenDraftSource = ref<{ id: string; questionHash: string; evidence: string } | null>(null)
const staleWrittenDraft = computed(() => !!writtenDraftSource.value && (
  writtenDraftSource.value.id !== saved.value?.id || writtenDraftSource.value.questionHash !== saved.value?.questionHash
  || !!saved.value?.confirmedMemoryId))
function beginWrittenDraft() {
  if (text.value === (currentVersion.value?.text ?? '')) { writtenDraftSource.value = null; return }
  if (!writtenDraftSource.value && saved.value)
    writtenDraftSource.value = { id: saved.value.id, questionHash: saved.value.questionHash, evidence: saved.value.originalEvidence }
}
function discardWrittenDraft() {
  writtenDraftSource.value = null
  text.value = currentVersion.value?.text ?? ''
}
const status = ref<MemoryStatus>('statement')
const error = ref('')
const playbackUrl = ref('')
const loadingPlayback = ref(false)
let uploadId = createSourceUploadId()
let generation = 0
let live = true
const currentVersion = computed(() => saved.value?.writtenVersions.find(x => x.id === saved.value?.representationId))
const dirty = computed(() => !!file.value || staleWrittenDraft.value || text.value !== (currentVersion.value?.text ?? ''))
const busy = computed(() => pending.value || recording.value)
watch(dirty, value => emit('dirty-change', value), { immediate: true, flush: 'sync' })
watch(busy, value => emit('busy', value), { immediate: true, flush: 'sync' })
watch(file, value => {
  uploadId = createSourceUploadId()
  draftSource.value = value ? nextDraftSource ?? sourceNow() : null
  nextDraftSource = null
}, { flush: 'sync' })
function message(cause: unknown) {
  const code = (cause as { response?: { status?: number } }).response?.status
  error.value = code === 409 ? 'The question or recording changed. Your draft is kept. Reload before continuing.'
    : code === 403 || code === 404 ? 'This recording or board is no longer available to you. Your draft is kept.'
      : code === 400 || code === 413 ? 'This recording or written version could not be accepted. Check the file format, 2 MiB size limit and text length.'
        : 'The request could not be confirmed. Keep your draft and retry, or reload to check what was saved.'
}
async function load() {
  if (!props.sourceReady || busy.value || loading.value) return
  const request = ++generation; loading.value = true; error.value = ''
  const hadDraft = dirty.value
  const initialText = text.value
  try {
    const next = await thinkingAudioApi.get(props.boardId, props.cardId, props.layerId)
    if (!live || request !== generation) return
    if (saved.value?.id !== next?.id && playbackUrl.value) { URL.revokeObjectURL(playbackUrl.value); playbackUrl.value = '' }
    saved.value = next; loaded.value = true
    if (!hadDraft && text.value === initialText) text.value = next?.writtenVersions.find(x => x.id === next.representationId)?.text ?? ''
  } catch (cause) { if (live && request === generation) { message(cause); loaded.value = false } }
  finally { if (live && request === generation) loading.value = false }
}
async function mutate(action: () => Promise<ThinkingAudio>, kind: 'upload' | 'write' | 'confirm') {
  if (!props.sourceReady || !loaded.value || busy.value || loading.value) return
  pending.value = true; error.value = ''; const request = ++generation
  try {
    const receipt = await action()
    if (!live || request !== generation) return
    saved.value = receipt
    if (kind === 'upload') file.value = null
    if (kind !== 'upload') { writtenDraftSource.value = null; text.value = currentVersion.value?.text ?? '' }
    if (kind === 'confirm') emit('confirmed')
  } catch (cause) { if (live && request === generation) message(cause) }
  finally { if (live && request === generation) pending.value = false }
}
function upload() {
  const draft = file.value; const source = draftSource.value
  if (!draft || !source || staleDraft.value) return
  const id = uploadId
  void mutate(() => thinkingAudioApi.upload(source.boardId, source.cardId, source.layerId, source.revision, id, draft), 'upload')
}
function write() {
  const receipt = saved.value; if (!receipt || staleWrittenDraft.value || !text.value.trim()) return
  const draft = text.value
  void mutate(() => thinkingAudioApi.write(receipt.id, receipt.revision, draft), 'write')
}
function confirm() {
  const receipt = saved.value; if (!receipt?.representationId || dirty.value) return
  void mutate(() => thinkingAudioApi.confirm(receipt.id, receipt.revision, props.revision, receipt.representationId!, status.value), 'confirm')
}
async function playback() {
  if (!saved.value || loadingPlayback.value) return
  loadingPlayback.value = true; error.value = ''
  try {
    const blob = await thinkingAudioApi.original(saved.value.id)
    if (!live) return
    if (playbackUrl.value) URL.revokeObjectURL(playbackUrl.value)
    playbackUrl.value = URL.createObjectURL(blob)
  } catch (cause) { if (live) message(cause) }
  finally { if (live) loadingPlayback.value = false }
}
watch(() => props.sourceReady, ready => { if (ready && !loaded.value) void load() })
watch(() => props.revision, () => { loaded.value = false; if (!busy.value) void load() })
onMounted(load)
onUnmounted(() => { live = false; generation++; if (playbackUrl.value) URL.revokeObjectURL(playbackUrl.value) })
</script>

<template>
  <section class="saved-audio" aria-label="Private audio answer">
    <p v-if="loading" role="status">Loading your recording…</p>
    <p v-if="error" role="alert">{{ error }}</p>
    <button v-if="error" type="button" :disabled="busy || loading || !sourceReady" @click="load">Reload saved recording</button>
    <section v-if="staleWrittenDraft" aria-label="Retained written audio draft">
      <p>Your recording or question changed. This draft still belongs to the earlier source. Copy it before discarding; it will not be saved against a different recording.</p>
      <label>Earlier written draft<textarea :value="text" readonly aria-label="Earlier written audio draft" rows="4" /></label>
      <details><summary>Draft's original question</summary><p class="verbatim">{{ writtenDraftSource?.evidence }}</p></details>
      <button type="button" :disabled="busy || loading" @click="discardWrittenDraft">Discard earlier written draft</button>
    </section>
    <template v-if="!saved">
      <AudioAnswerRecorder v-model="file" :disabled="pending || loading || !loaded || !sourceReady" @draft-started="beginDraft" @busy="recording = $event" />
      <p v-if="staleDraft" role="status">The question changed after this audio draft began. Replay or download it, then discard it or record a new answer for the current question.</p>
      <button type="button" :disabled="!file || staleDraft || busy || loading || !loaded || !sourceReady" @click="upload">Save original privately</button>
    </template>
    <template v-else>
      <p><strong>Original recording saved privately</strong> · {{ saved.fileName }} · {{ (saved.byteSize / 1024).toFixed(1) }} KiB</p>
      <p v-if="!saved.representationId">Untranscribed · {{ answerAlreadyKept ? 'A separate private answer is already kept.' : 'This question is still unanswered.' }} The original is ready to replay or download.</p>
      <p v-else-if="!saved.confirmedMemoryId">Written version saved · {{ answerAlreadyKept ? 'A separate private answer is already kept. Correct it in private memory.' : 'The question stays unanswered until you confirm it below.' }}</p>
      <p v-else>Confirmed by you and kept in private memory. Confirmation records your choice; it is not external verification.</p>
      <button type="button" :disabled="loadingPlayback" @click="playback">{{ loadingPlayback ? 'Loading original…' : 'Load original for playback or download' }}</button>
      <template v-if="playbackUrl">
        <!-- Raw user input may have no transcription. Saved written alternatives appear below; no timed captions are fabricated. -->
        <!-- eslint-disable-next-line vuejs-accessibility/media-has-caption -->
        <audio :src="playbackUrl" controls preload="metadata" aria-label="Play saved original recording" />
        <a :href="playbackUrl" :download="saved.fileName">Download original recording</a>
      </template>
      <details><summary>Original question evidence</summary><p class="verbatim">{{ saved.originalEvidence }}</p></details>
      <template v-if="!saved.confirmedMemoryId && !staleWrittenDraft">
        <label>Written version of your recording<textarea v-model="text" aria-label="Written audio version" :disabled="pending" maxlength="8000" rows="4" @input="beginWrittenDraft" /></label>
        <p>Write or paste your own version. Nothing is sent to a transcription provider. Keep uncertain words explicit.</p>
        <button type="button" :disabled="!text.trim() || !dirty || busy || loading || !loaded || !sourceReady" @click="write">Save written version</button>
        <label>How to treat the confirmed answer<select v-model="status" aria-label="Audio answer status" :disabled="pending"><option value="statement">Statement</option><option value="assumption">Assumption</option><option value="unknown">Unknown</option><option value="needsReview">Needs review</option></select></label>
        <button type="button" :disabled="!saved.representationId || dirty || busy || loading || !loaded || !sourceReady || answerAlreadyKept" @click="confirm">Confirm written version as my answer</button>
      </template>
      <RouterLink v-else-if="saved.confirmedMemoryId" :to="{ path: '/workspace/memory', query: { boardId } }">Review or correct the confirmed answer in private memory</RouterLink>
      <details v-if="saved.writtenVersions.length"><summary>Saved written versions ({{ saved.writtenVersions.length }})</summary><ol><li v-for="version in saved.writtenVersions" :key="version.id"><strong>{{ version.quality === 'Verified' ? 'Confirmed by you' : version.quality === 'Superseded' ? 'Previous version' : 'Written, unconfirmed' }}</strong><p class="verbatim">{{ version.text }}</p></li></ol></details>
      <template v-if="file"><AudioAnswerRecorder :model-value="file" disabled /><p>Reloading the saved recording keeps this local draft while this page stays open. A browser refresh or closing the page loses an unuploaded file. <button type="button" :disabled="busy" @click="file = null">Discard this local audio draft</button></p></template>
    </template>
    <p v-if="pending" role="status">Saving… Wait here for the receipt. Closing this page does not cancel a request already received by the server.</p>
  </section>
</template>

<style scoped>
.saved-audio { display: grid; gap: .75rem; min-width: 0; margin-top: 1rem; }
p { margin: 0; line-height: 1.5; overflow-wrap: anywhere; }
label { display: grid; gap: .35rem; }
textarea,select,button { color: var(--td-text-primary); background: var(--td-surface-container); border: 1px solid var(--td-border-default); border-radius: .4rem; padding: .55rem; }
textarea,audio { width: 100%; max-width: 100%; }
button { justify-self: start; text-align: left; }
button:disabled { opacity: .5; }
.verbatim { white-space: pre-wrap; }
a { text-decoration: underline; }
:focus-visible { outline: 2px solid var(--td-text-primary); outline-offset: 2px; }
</style>
