<script setup lang="ts">
import { computed, onUnmounted, ref, watch } from 'vue'

const props = defineProps<{ modelValue: File | null; disabled?: boolean }>()
const emit = defineEmits<{ 'update:modelValue': [file: File | null]; busy: [busy: boolean]; 'draft-started': [] }>()
const maximumBytes = 2 * 1024 * 1024
const maximumSeconds = 60
const error = ref('')
const requesting = ref(false)
const recording = ref(false)
const playbackUrl = ref('')
const supported = computed(() => typeof MediaRecorder !== 'undefined' && !!navigator.mediaDevices?.getUserMedia)
let generation = 0
let recorder: MediaRecorder | null = null
let stream: MediaStream | null = null
let timer: ReturnType<typeof setTimeout> | undefined

watch(() => props.modelValue, file => {
  if (playbackUrl.value) URL.revokeObjectURL(playbackUrl.value)
  playbackUrl.value = file ? URL.createObjectURL(file) : ''
}, { immediate: true })
watch([requesting, recording], () => emit('busy', requesting.value || recording.value), { flush: 'sync' })

function releaseTracks() {
  clearTimeout(timer)
  stream?.getTracks().forEach(track => track.stop())
  stream = null
}
function cancel() {
  generation++
  if (recorder && recorder.state !== 'inactive') recorder.stop()
  recorder = null
  releaseTracks()
  requesting.value = false
  recording.value = false
}
function stop() {
  if (recorder?.state === 'recording') recorder.stop()
}
function accept(file: File) {
  if (!file.size || file.size > maximumBytes) {
    error.value = 'Choose a non-empty recording no larger than 2 MiB.'
    return
  }
  if (!/^audio\/(webm|ogg|wav|x-wav|mpeg|mp4)(;|$)/i.test(file.type)) {
    error.value = 'Choose WebM, Ogg, WAV, MP3 or M4A audio.'
    return
  }
  error.value = ''
  emit('update:modelValue', file)
}
function choose(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!props.disabled && !requesting.value && !recording.value && file) { emit('draft-started'); accept(file) }
}
async function start() {
  if (props.disabled || requesting.value || recording.value || !supported.value) return
  emit('draft-started')
  error.value = ''
  const request = ++generation
  requesting.value = true
  try {
    const acquired = await navigator.mediaDevices.getUserMedia({ audio: true })
    if (request !== generation) {
      acquired.getTracks().forEach(track => track.stop())
      return
    }
    stream = acquired
    const mimeType = ['audio/webm;codecs=opus', 'audio/ogg;codecs=opus', 'audio/mp4'].find(type => MediaRecorder.isTypeSupported(type))
    const active = new MediaRecorder(acquired, mimeType ? { mimeType, audioBitsPerSecond: 48000 } : undefined)
    recorder = active
    const chunks: Blob[] = []
    let size = 0
    active.ondataavailable = event => {
      if (request !== generation || !event.data.size) return
      size += event.data.size
      if (size > maximumBytes) {
        error.value = 'This recording exceeded 2 MiB. Try a shorter recording or choose a smaller file.'
        cancel()
        return
      }
      chunks.push(event.data)
    }
    active.onerror = () => {
      if (request !== generation) return
      error.value = 'Recording failed. Try again or choose an audio file; your previous draft is still available.'
      cancel()
    }
    active.onstop = () => {
      if (request !== generation) return
      releaseTracks()
      recorder = null
      recording.value = false
      const type = active.mimeType || chunks[0]?.type || 'audio/webm'
      const extension = type.includes('mp4') ? 'm4a' : type.includes('ogg') ? 'ogg' : 'webm'
      accept(new File(chunks, `answer.${extension}`, { type }))
    }
    active.start(250)
    recording.value = true
    requesting.value = false
    timer = setTimeout(stop, maximumSeconds * 1000)
  } catch {
    if (request !== generation) return
    cancel()
    error.value = 'The microphone could not be opened. Check browser permission and try again, or choose an audio file.'
  }
}
onUnmounted(() => {
  cancel()
  if (playbackUrl.value) URL.revokeObjectURL(playbackUrl.value)
})
</script>

<template>
  <section class="audio-answer" aria-label="Original audio answer">
    <p>Keep a recording as your original answer. Audio alone is not transcribed or understood. You can add and confirm a written representation separately.</p>
    <p v-if="!supported">Microphone recording needs a supported browser and a secure connection. You can still choose an audio file.</p>
    <div class="audio-answer__actions">
      <button v-if="!requesting && !recording" type="button" :disabled="disabled || !supported" @click="start">Record audio</button>
      <button v-if="recording" type="button" @click="stop">Stop recording</button>
      <button v-if="requesting || recording" type="button" @click="cancel">Cancel recording</button>
      <label>Choose audio file<input type="file" accept="audio/webm,audio/ogg,audio/wav,audio/x-wav,audio/mpeg,audio/mp4" :disabled="disabled || requesting || recording" @change="choose"></label>
    </div>
    <p v-if="requesting || recording" role="status">{{ requesting ? 'Waiting for microphone permission…' : 'Recording… Stops after 60 seconds.' }}</p>
    <p v-if="error" role="alert">{{ error }}</p>
    <template v-if="modelValue && playbackUrl">
      <p>{{ modelValue.name }} · {{ (modelValue.size / 1024).toFixed(1) }} KiB · unsaved original</p>
      <!-- This is the user's untranscribed input. A caption track cannot be invented before a written version exists. -->
      <!-- eslint-disable-next-line vuejs-accessibility/media-has-caption -->
      <audio :src="playbackUrl" controls preload="metadata" aria-label="Play your original audio draft" />
      <div class="audio-answer__actions"><a :href="playbackUrl" :download="modelValue.name">Download audio draft</a><button type="button" :disabled="disabled || requesting || recording" @click="emit('update:modelValue', null)">Remove audio draft</button></div>
    </template>
    <p>Maximum 2 MiB per file. Recording stays in this draft until you explicitly save it.</p>
  </section>
</template>

<style scoped>
.audio-answer { display: grid; gap: .7rem; min-width: 0; border: 1px solid var(--td-border-default); border-radius: .5rem; padding: .75rem; }
.audio-answer p { margin: 0; font-size: .85rem; line-height: 1.5; overflow-wrap: anywhere; }
.audio-answer__actions { display: flex; flex-wrap: wrap; align-items: center; gap: .6rem; }
audio, input { max-width: 100%; }
label { display: grid; gap: .3rem; font-size: .85rem; }
button, a { padding: .5rem; color: var(--td-text-primary); }
button { background: var(--td-surface-secondary); border: 1px solid var(--td-border-default); border-radius: .3rem; }
</style>
