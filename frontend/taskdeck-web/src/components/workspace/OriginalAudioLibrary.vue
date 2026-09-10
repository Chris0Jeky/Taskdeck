<script setup lang="ts">
import { computed, onScopeDispose, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import { thinkingAudioApi, type AudioLibraryDetail, type AudioLibraryPage } from '../../api/thinkingAudioApi'
import { useSessionStore } from '../../store/sessionStore'

const session = useSessionStore()
const page = ref<AudioLibraryPage | null>(null)
const detail = ref<AudioLibraryDetail | null>(null)
const pageOffset = ref(0)
const previousOffsets = ref<number[]>([])
const loading = ref(false)
const loadingDetail = ref(false)
const loadingAudio = ref(false)
const error = ref('')
const playbackUrl = ref('')
const authenticated = computed(() => !!session.userId && !!session.token)
let pageGeneration = 0
let detailGeneration = 0

function clearDetail() {
  detailGeneration++; detail.value = null; loadingDetail.value = false; loadingAudio.value = false
  if (playbackUrl.value) URL.revokeObjectURL(playbackUrl.value)
  playbackUrl.value = ''
}
function clear() {
  pageGeneration++; page.value = null; loading.value = false; error.value = ''
  pageOffset.value = 0; previousOffsets.value = []; clearDetail()
}
watch(() => [session.userId, authenticated.value], clear, { flush: 'sync' })
onScopeDispose(clear)
function message(cause: unknown) {
  const status = (cause as { response?: { status?: number } }).response?.status
  return status === 403 || status === 404
    ? 'This private recording is no longer available to you. Reload the library to check access.'
    : 'Unable to load recordings. Your saved originals are unchanged. Try again.'
}
async function load(offset = 0, direction: 'reset' | 'next' | 'back' = 'reset') {
  if (!authenticated.value || loading.value) return
  const request = ++pageGeneration
  loading.value = true; error.value = ''; clearDetail()
  try {
    const result = await thinkingAudioApi.library(offset)
    if (request !== pageGeneration) return
    if (direction === 'next') previousOffsets.value.push(pageOffset.value)
    else if (direction === 'back') previousOffsets.value.pop()
    else previousOffsets.value = []
    page.value = result; pageOffset.value = offset
  } catch (cause) { if (request === pageGeneration) error.value = message(cause) }
  finally { if (request === pageGeneration) loading.value = false }
}
async function inspect(id: string) {
  if (!authenticated.value || loading.value) return
  clearDetail(); const request = detailGeneration
  loadingDetail.value = true; error.value = ''
  try {
    const result = await thinkingAudioApi.libraryDetail(id)
    if (request !== detailGeneration) return
    if (result.recording.id !== id) throw new Error('Unexpected recording')
    detail.value = result
  } catch (cause) { if (request === detailGeneration) error.value = message(cause) }
  finally { if (request === detailGeneration) loadingDetail.value = false }
}
async function playback() {
  if (!detail.value || !authenticated.value || loadingAudio.value) return
  const request = detailGeneration; const id = detail.value.recording.id
  loadingAudio.value = true; error.value = ''
  try {
    const blob = await thinkingAudioApi.libraryOriginal(id)
    if (request !== detailGeneration) return
    if (playbackUrl.value) URL.revokeObjectURL(playbackUrl.value)
    playbackUrl.value = URL.createObjectURL(blob)
  } catch (cause) {
    if (request === detailGeneration) { clearDetail(); error.value = message(cause) }
  } finally { if (request === detailGeneration) loadingAudio.value = false }
}
</script>

<template>
  <section class="original-library" aria-label="Original recordings">
    <h2>Original recordings</h2>
    <p>Your private audio originals remain separate from written answers. Find earlier recordings even when a question changes or is removed. Deleted-board originals stay here and in account export until account deletion; recordings on existing boards require your current access.</p>
    <button type="button" :disabled="loading || !authenticated" @click="load()">{{ loading ? 'Loading recordings…' : page ? 'Reload recording library' : 'Browse original recordings' }}</button>
    <p v-if="error" role="alert">{{ error }}</p>
    <p v-if="loading || loadingDetail" role="status">{{ loading ? 'Loading recording library…' : 'Loading recording details…' }}</p>
    <template v-if="page">
      <p v-if="!page.items.length">{{ page.nextOffset !== null ? 'No accessible recordings on this page. Continue to the next page.' : 'No accessible recordings on this page. Save an original from a Thinking Deck question to keep it here.' }}</p>
      <ul class="original-library__list">
        <li v-for="item in page.items" :key="item.id">
          <strong>{{ item.fileName }}</strong> · {{ (item.byteSize / 1024).toFixed(1) }} KiB
          <p>{{ item.hasConfirmedAnswer ? (item.boardRemoved ? 'Previously confirmed; written version retained' : 'Confirmed answer kept separately') : item.hasWrittenVersion ? 'Written version, unconfirmed' : 'Untranscribed original' }}<span v-if="item.boardRemoved"> · Board removed</span></p>
          <p class="verbatim">{{ item.questionExcerpt }}</p>
          <button type="button" :disabled="loading" :aria-label="`Inspect ${item.fileName}: ${item.questionExcerpt}`" @click="inspect(item.id)">Inspect recording</button>
        </li>
      </ul>
      <nav aria-label="Recording library pages">
        <button v-if="previousOffsets.length" type="button" :disabled="loading" @click="load(previousOffsets[previousOffsets.length - 1]!, 'back')">Previous recordings</button>
        <button v-if="page.nextOffset !== null" type="button" :disabled="loading" @click="load(page.nextOffset!, 'next')">Next recordings</button>
      </nav>
    </template>
    <article v-if="detail" class="original-library__detail" aria-label="Selected original recording">
      <h3>{{ detail.recording.fileName }}</h3>
      <button type="button" :disabled="loadingAudio" @click="playback">{{ loadingAudio ? 'Loading original…' : 'Load selected original for playback or download' }}</button>
      <template v-if="playbackUrl">
        <!-- Raw retained audio may be untranscribed. Written alternatives below are not fabricated timed captions. -->
        <!-- eslint-disable-next-line vuejs-accessibility/media-has-caption -->
        <audio :src="playbackUrl" controls preload="metadata" aria-label="Play selected original recording" />
        <a :href="playbackUrl" :download="detail.recording.fileName">Download selected original</a>
      </template>
      <h4>Original question evidence</h4><p class="verbatim">{{ detail.recording.originalEvidence }}</p>
      <p v-if="!detail.recording.writtenVersions.length">No written version has been saved. This original has not been automatically transcribed.</p>
      <details v-else><summary>Retained written versions ({{ detail.recording.writtenVersions.length }})</summary>
        <ol><li v-for="version in detail.recording.writtenVersions" :key="version.id"><strong>{{ version.quality === 'Verified' ? 'Confirmed by you' : version.quality === 'Superseded' ? 'Previous written version' : 'Written, unconfirmed' }}</strong><p class="verbatim">{{ version.text }}</p></li></ol>
      </details>
      <RouterLink v-if="detail.currentBoardId && detail.currentCardId" :to="`/workspace/boards/${detail.currentBoardId}/cards/${detail.currentCardId}/thinking`">Open the current question to continue</RouterLink>
      <p v-else>The original question is no longer current or its board is archived or removed. This retained copy is read-only.</p>
      <details><summary>Original source receipt</summary><p>Capture {{ detail.recording.captureId }}</p><p>Asset {{ detail.recording.sourceAssetId }}</p><p>SHA256 {{ detail.recording.contentHash }}</p></details>
    </article>
  </section>
</template>

<style scoped>
.original-library { display: grid; gap: .8rem; margin-block: 1.5rem; padding: 1rem; border: 1px solid var(--td-border-default); border-radius: .65rem; background: var(--td-surface-raised); color: var(--td-text-primary); min-width: 0; }
h2,h3,h4,p { margin: 0; overflow-wrap: anywhere; }
p { line-height: 1.6; }
button { justify-self: start; padding: .6rem .8rem; color: var(--td-text-primary); background: var(--td-surface-container); border: 1px solid var(--td-border-default); border-radius: .4rem; text-align: left; }
button:disabled { opacity: .5; }
.original-library__list { display: grid; gap: 1rem; padding-left: 1.2rem; }
.original-library__list li { min-width: 0; overflow-wrap: anywhere; }
.original-library__detail { display: grid; gap: .8rem; border-top: 1px solid var(--td-border-default); padding-top: 1rem; min-width: 0; }
.verbatim { white-space: pre-wrap; }
nav { display: flex; gap: .75rem; flex-wrap: wrap; }
audio { width: 100%; max-width: 100%; }
a { text-decoration: underline; }
[role=alert] { color: var(--td-color-error); }
:focus-visible { outline: 2px solid var(--td-text-primary); outline-offset: 2px; }
</style>
