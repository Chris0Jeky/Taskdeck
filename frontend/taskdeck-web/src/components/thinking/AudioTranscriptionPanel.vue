<script setup lang="ts">
import { computed, onScopeDispose, ref, watch } from 'vue'
import { audioTranscriptionApi, type TranscriptionRequest, type TranscriptionStatus, type TranscriptionReceipt } from '../../api/audioTranscriptionApi'
import type { ThinkingAudio } from '../../api/thinkingAudioApi'
import { useSessionStore } from '../../store/sessionStore'
import { createSourceUploadId } from '../../utils/sourceUploadId'

const props = defineProps<{ audio: ThinkingAudio; disabled?: boolean; canAdopt?: boolean }>()
const emit = defineEmits<{ adopt: [candidate: { id: string; text: string }]; busy: [value: boolean] }>()
const session = useSessionStore()
const authenticated = computed(() => !!session.userId && !!session.token)
const status = ref<TranscriptionStatus | null>(null)
const consentedConfigurationHash = ref<string | null>(null)
const consent = computed({
  get: () => !!consentedConfigurationHash.value && consentedConfigurationHash.value === status.value?.configuration.configurationHash,
  set: (value: boolean) => { consentedConfigurationHash.value = value ? status.value?.configuration.configurationHash ?? null : null },
})
const loading = ref(false)
const pending = ref(false)
const error = ref('')
const needsRefresh = ref(false)
const retry = ref<TranscriptionRequest | null>(null)
let generation = 0
const running = computed(() => status.value?.attempts.some(x => x.state === 'Running'))
const available = computed(() => status.value?.configuration.enabled && !running.value && !needsRefresh.value
  && (retry.value || (status.value.attemptsUsedToday < status.value.configuration.dailyAttempts
    && status.value.inputBytesUsedToday + props.audio.byteSize <= status.value.configuration.dailyInputBytes && status.value.attempts.length < 20)))
function clear() {
  generation++; status.value = null; consent.value = false; loading.value = false; pending.value = false
  error.value = ''; retry.value = null; needsRefresh.value = false; emit('busy', false)
}
watch(() => [session.userId, authenticated.value, props.audio.id, props.audio.revision], clear, { flush: 'sync' })
onScopeDispose(clear)
function message(cause: unknown) {
  const code = (cause as { response?: { status?: number } }).response?.status
  return code === 403 || code === 404 ? 'This recording is no longer available to you. Reload the recording library.'
    : code === 409 ? 'The recording, destination or attempt changed. Refresh the receipts before continuing.'
      : code === 400 ? 'This request was not accepted. Refresh to check the destination, limits and any saved receipt.'
        : 'The result could not be confirmed. Refresh receipts before retrying; a received request may still finish.'
}
function accept(receipt: TranscriptionReceipt) {
  if (receipt.audioAnswerId !== props.audio.id) throw new Error('Unexpected recording receipt')
  if (status.value) status.value.attempts = [receipt, ...status.value.attempts.filter(x => x.id !== receipt.id)]
}
async function refresh() {
  if (!authenticated.value || props.disabled || loading.value || pending.value) return
  const request = ++generation; const id = props.audio.id; loading.value = true; error.value = ''
  try {
    const value = await audioTranscriptionApi.status(id)
    if (request !== generation) return
    if (value.attempts.some(x => x.audioAnswerId !== id)) throw new Error('Unexpected recording receipts')
    if (status.value?.configuration.configurationHash !== value.configuration.configurationHash) consent.value = false
    status.value = value; needsRefresh.value = false
    if (retry.value && value.attempts.some(x => x.requestId === retry.value?.requestId)) { retry.value = null; consent.value = false }
    if (!value.configuration.enabled || (retry.value && retry.value.configurationHash !== value.configuration.configurationHash)) consent.value = false
  } catch (cause) { if (request === generation) { error.value = message(cause); needsRefresh.value = true; consent.value = false } }
  finally { if (request === generation) loading.value = false }
}
async function start() {
  if (!authenticated.value || props.disabled || loading.value || pending.value || !available.value || !consent.value || !status.value) return
  const body = retry.value ?? { requestId: createSourceUploadId(), expectedRevision: props.audio.revision, configurationHash: status.value.configuration.configurationHash }
  if (body.configurationHash !== status.value.configuration.configurationHash) return
  retry.value = body; pending.value = true; emit('busy', true); error.value = ''
  const request = ++generation; const id = props.audio.id
  try {
    const receipt = await audioTranscriptionApi.start(id, body)
    if (request !== generation) return
    if (receipt.requestId !== body.requestId) throw new Error('Unexpected request receipt')
    accept(receipt); retry.value = null; consent.value = false; needsRefresh.value = true
  } catch (cause) { if (request === generation) { error.value = message(cause); needsRefresh.value = true; consent.value = false } }
  finally { if (request === generation) { pending.value = false; emit('busy', false) } }
}
function adopt(receipt: TranscriptionReceipt) {
  if (!props.canAdopt || props.disabled || pending.value || loading.value || receipt.state !== 'Completed' || !receipt.representationId || !receipt.text) return
  emit('adopt', { id: receipt.representationId, text: receipt.text })
}
function failure(receipt: TranscriptionReceipt) {
  if (receipt.state === 'Running') return 'Processing is underway. Refresh to check its receipt. Leaving this page does not cancel the request.'
  if (receipt.state === 'Expired') return 'The attempt ended without a publishable result. Refresh, then request a new attempt explicitly if needed.'
  if (receipt.failureCode === 'access-changed') return 'Recording access changed; no transcript was published.'
  if (receipt.failureCode === 'input-unavailable') return 'The original could not be read completely; no transcript was published.'
  if (receipt.failureCode === 'provider-response') return 'The provider returned an invalid or oversized transcript; no text was accepted.'
  return 'The provider was unavailable or timed out. The original remains saved. A retry uses another attempt.'
}
</script>

<template>
  <section class="transcription" aria-label="Automatic transcription">
    <h4>Automatic transcription</h4>
    <p>Request a provisional transcript of this recording, then compare it with the original before using it. Uploading or writing an answer does not send audio automatically.</p>
    <button type="button" :disabled="disabled || !authenticated || loading || pending" @click="refresh">{{ loading ? 'Loading transcription receipts…' : status ? 'Refresh transcription receipts' : 'Transcription options and receipts' }}</button>
    <p v-if="error" role="alert">{{ error }}</p>
    <template v-if="status">
      <p v-if="!status.configuration.enabled">Automatic transcription is not configured or is paused. You can still replay the original and write your own version.</p>
      <template v-else>
        <p>Destination: <strong>{{ status.configuration.origin }}</strong> · Model: <strong>{{ status.configuration.model }}</strong></p>
        <p>{{ status.attemptsUsedToday }} of {{ status.configuration.dailyAttempts }} daily attempts used · {{ (status.inputBytesUsedToday / 1024).toFixed(1) }} of {{ (status.configuration.dailyInputBytes / 1024).toFixed(1) }} KiB used today (UTC). These limits do not measure recording duration or provider charges. Up to 20 attempts are retained per recording.</p>
        <p v-if="retry">A previous request is unconfirmed. Retrying below reuses its request receipt; it does not create a second attempt.</p>
        <p v-if="retry && retry.configurationHash !== status.configuration.configurationHash" role="status">The destination changed. Reload this page before preparing a new request; earlier attempts remain in their receipts.</p>
        <label><input v-model="consent" type="checkbox" :disabled="disabled || pending || loading || !available || (retry !== null && retry.configurationHash !== status.configuration.configurationHash)">Send this original recording to the displayed destination for this transcription request.</label>
        <button type="button" :disabled="disabled || pending || loading || !available || !consent || (retry !== null && retry.configurationHash !== status.configuration.configurationHash)" @click="start">{{ pending ? 'Requesting transcript…' : retry ? 'Retry the same transcription request' : 'Request a transcript' }}</button>
      </template>
      <p v-if="needsRefresh && !error" role="status">Refresh receipts before requesting another attempt.</p>
      <ol v-if="status.attempts.length">
        <li v-for="receipt in status.attempts" :key="receipt.id">
          <strong>{{ receipt.state === 'Completed' ? 'Provisional transcript' : receipt.state }}</strong> · {{ receipt.model }}
          <template v-if="receipt.state === 'Completed' && receipt.text">
            <p class="verbatim">{{ receipt.text }}</p>
            <button v-if="canAdopt" type="button" :disabled="disabled || pending || loading" @click="adopt(receipt)">Use this transcript as a written draft</button>
            <p>Unconfirmed. Review uncertain words; using the text as a draft does not answer the question.</p>
          </template>
          <p v-else role="status">{{ failure(receipt) }}</p>
        </li>
      </ol>
    </template>
  </section>
</template>

<style scoped>
.transcription { display: grid; gap: .7rem; padding: .9rem; min-width: 0; border: 1px solid var(--td-border-default); border-radius: .5rem; background: var(--td-surface-raised); color: var(--td-text-primary); }
h4,p { margin: 0; overflow-wrap: anywhere; line-height: 1.5; }
label { display: flex; gap: .6rem; align-items: start; }
input { margin-top: .25rem; flex-shrink: 0; }
button { justify-self: start; text-align: left; padding: .6rem; color: var(--td-text-primary); background: var(--td-surface-container); border: 1px solid var(--td-border-default); border-radius: .4rem; }
button:disabled { opacity: .5; }
ol { display: grid; gap: 1rem; padding-left: 1.2rem; min-width: 0; }
li { min-width: 0; overflow-wrap: anywhere; }
.verbatim { white-space: pre-wrap; }
[role=alert] { color: var(--td-color-error); }
:focus-visible { outline: 2px solid var(--td-text-primary); outline-offset: 2px; }
</style>
