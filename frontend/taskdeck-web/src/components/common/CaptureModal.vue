<script setup lang="ts">
import { nextTick, onMounted, onUnmounted, ref } from 'vue'
import { useCaptureStore } from '../../store/captureStore'
import { registerEscapeHandler } from '../../composables/useEscapeStack'
import { usePerformanceMark } from '../../composables/usePerformanceMark'

const MAX_TRANSCRIPT_LENGTH = 51_200

const props = defineProps<{
  boardId?: string | null
  boardName?: string | null
}>()

const emit = defineEmits<{
  close: []
  created: [itemId: string]
}>()

const captureStore = useCaptureStore()

type CaptureMode = 'typed' | 'transcript'
const captureMode = ref<CaptureMode>('typed')
const text = ref('')
const saving = ref(false)
const inlineError = ref<string | null>(null)
const textInput = ref<HTMLTextAreaElement | null>(null)
const transcriptInput = ref<HTMLTextAreaElement | null>(null)
const fileInput = ref<HTMLInputElement | null>(null)
const uploadedFileName = ref<string | null>(null)
const modalOpenPerf = usePerformanceMark('modal-open')
let unregisterEscapeHandler: (() => void) | null = null

modalOpenPerf.start()

function requestClose() {
  if (saving.value) {
    return
  }

  emit('close')
}

function handleKeydown(event: KeyboardEvent) {
  if ((event.ctrlKey || event.metaKey) && event.key === 'Enter') {
    event.preventDefault()
    void submit()
  }
}

function switchMode(mode: CaptureMode) {
  if (saving.value) {
    return
  }

  captureMode.value = mode
  inlineError.value = null

  nextTick(() => {
    if (mode === 'typed') {
      textInput.value?.focus()
    } else {
      transcriptInput.value?.focus()
    }
  })
}

function handleFileUpload(event: Event) {
  const target = event.target as HTMLInputElement
  const file = target.files?.[0]
  if (!file) {
    return
  }

  if (!file.name.endsWith('.txt') && file.type !== 'text/plain') {
    inlineError.value = 'Only .txt files are supported for transcript upload.'
    target.value = ''
    return
  }

  if (file.size > MAX_TRANSCRIPT_LENGTH) {
    inlineError.value = `File is too large. Maximum size is ${Math.floor(MAX_TRANSCRIPT_LENGTH / 1024)}KB.`
    target.value = ''
    return
  }

  const reader = new FileReader()
  reader.onload = () => {
    const content = reader.result as string
    if (content.length > MAX_TRANSCRIPT_LENGTH) {
      inlineError.value = `Transcript text is too long. Maximum length is ${MAX_TRANSCRIPT_LENGTH.toLocaleString()} characters.`
      target.value = ''
      return
    }

    text.value = content
    uploadedFileName.value = file.name
    inlineError.value = null
  }
  reader.onerror = () => {
    inlineError.value = 'Failed to read file. Please try again.'
    target.value = ''
  }
  reader.readAsText(file)
}

function triggerFileUpload() {
  fileInput.value?.click()
}

function clearFile() {
  uploadedFileName.value = null
  text.value = ''
  if (fileInput.value) {
    fileInput.value.value = ''
  }
}

async function submit() {
  if (saving.value) {
    return
  }

  const normalizedText = text.value.trim()
  if (!normalizedText) {
    inlineError.value = 'Capture text is required.'
    return
  }

  if (captureMode.value === 'transcript' && normalizedText.length > MAX_TRANSCRIPT_LENGTH) {
    inlineError.value = `Transcript text is too long. Maximum length is ${MAX_TRANSCRIPT_LENGTH.toLocaleString()} characters.`
    return
  }

  const source = captureMode.value === 'typed'
    ? 'Typed' as const
    : (uploadedFileName.value ? 'TranscriptFile' as const : 'TranscriptPaste' as const)

  try {
    saving.value = true
    inlineError.value = null
    const created = await captureStore.createItem({
      boardId: props.boardId ?? null,
      text: normalizedText,
      source,
    })
    text.value = ''
    uploadedFileName.value = null
    emit('created', created.id)
    emit('close')
  } catch {
    inlineError.value = captureStore.actionError ?? 'Failed to save capture item.'
  } finally {
    saving.value = false
  }
}

onMounted(async () => {
  unregisterEscapeHandler = registerEscapeHandler(requestClose)
  await nextTick()
  textInput.value?.focus()
  modalOpenPerf.end()
})

onUnmounted(() => {
  unregisterEscapeHandler?.()
  unregisterEscapeHandler = null
})
</script>

<template>
  <!-- eslint-disable-next-line vuejs-accessibility/no-static-element-interactions -- modal backdrop with dialog role and escape key handler; click-to-close is standard modal UX -->
  <div class="td-overlay" role="dialog" aria-label="Capture item" aria-modal="true" @click.self="requestClose" @keydown.escape="requestClose">
    <!-- eslint-disable-next-line vuejs-accessibility/no-static-element-interactions -- document role inside dialog; @keydown handles Ctrl+Enter save shortcut -->
    <div class="td-capture-modal" role="document" @keydown="handleKeydown">
      <header class="td-capture-modal__header">
        <h2>{{ captureMode === 'typed' ? 'Quick Capture' : 'Transcript Capture' }}</h2>
        <button class="td-capture-modal__close" aria-label="Close capture modal" @click="requestClose">
          X
        </button>
      </header>

      <nav class="td-capture-modal__tabs" role="tablist" aria-label="Capture mode">
        <button
          role="tab"
          :aria-selected="captureMode === 'typed'"
          :class="['td-capture-modal__tab', { 'td-capture-modal__tab--active': captureMode === 'typed' }]"
          :disabled="saving"
          @click="switchMode('typed')"
        >
          Quick Capture
        </button>
        <button
          role="tab"
          :aria-selected="captureMode === 'transcript'"
          :class="['td-capture-modal__tab', { 'td-capture-modal__tab--active': captureMode === 'transcript' }]"
          :disabled="saving"
          @click="switchMode('transcript')"
        >
          Transcript
        </button>
      </nav>

      <template v-if="captureMode === 'typed'">
        <p class="td-capture-modal__hint">
          Write or paste anything. Press Ctrl/Cmd+Enter to save.
          <span v-if="props.boardName">This capture will stay linked to {{ props.boardName }}.</span>
        </p>

        <textarea
          ref="textInput"
          v-model="text"
          aria-label="Capture text"
          class="td-capture-modal__input"
          placeholder="Capture a thought, task, or follow-up..."
          rows="8"
        />
      </template>

      <template v-else>
        <p class="td-capture-modal__hint">
          Paste a meeting transcript, conversation log, or notes below, or upload a .txt file.
          <span v-if="props.boardName">This capture will stay linked to {{ props.boardName }}.</span>
        </p>

        <div class="td-capture-modal__file-bar">
          <button
            class="td-btn td-btn--secondary td-btn--sm"
            :disabled="saving"
            @click="triggerFileUpload"
          >
            Upload .txt file
          </button>
          <span v-if="uploadedFileName" class="td-capture-modal__file-name">
            {{ uploadedFileName }}
            <button
              class="td-capture-modal__file-clear"
              aria-label="Clear uploaded file"
              :disabled="saving"
              @click="clearFile"
            >
              X
            </button>
          </span>
          <input
            ref="fileInput"
            type="file"
            accept=".txt,text/plain"
            aria-label="Upload text file"
            class="td-capture-modal__file-input"
            @change="handleFileUpload"
          />
        </div>

        <textarea
          ref="transcriptInput"
          v-model="text"
          aria-label="Transcript content"
          class="td-capture-modal__input td-capture-modal__input--transcript"
          placeholder="Paste transcript content here..."
          rows="14"
        />

        <p class="td-capture-modal__char-count" :class="{ 'td-capture-modal__char-count--warn': text.length > MAX_TRANSCRIPT_LENGTH * 0.9 }">
          {{ text.length.toLocaleString() }} / {{ MAX_TRANSCRIPT_LENGTH.toLocaleString() }} characters
        </p>
      </template>

      <div v-if="inlineError" class="td-alert td-alert--error" role="alert">
        {{ inlineError }}
      </div>

      <footer class="td-capture-modal__actions">
        <button class="td-btn td-btn--secondary" :disabled="saving" @click="requestClose">
          Cancel
        </button>
        <button class="td-btn td-btn--primary" :disabled="saving" @click="submit">
          {{ saving ? 'Saving...' : 'Save Capture' }}
        </button>
      </footer>
    </div>
  </div>
</template>

<style scoped>
.td-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 60;
  padding: var(--td-space-4);
}

.td-capture-modal {
  width: min(680px, 100%);
  background: var(--td-surface-primary);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-xl);
  box-shadow: var(--td-shadow-xl);
  padding: var(--td-space-5);
  display: flex;
  flex-direction: column;
  gap: var(--td-space-3);
}

.td-capture-modal__header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.td-capture-modal__header h2 {
  font-size: var(--td-font-xl);
  margin: 0;
}

.td-capture-modal__close {
  border: 1px solid var(--td-border-default);
  background: transparent;
  color: var(--td-text-secondary);
  border-radius: var(--td-radius-md);
  padding: 2px 8px;
  cursor: pointer;
}

.td-capture-modal__tabs {
  display: flex;
  gap: var(--td-space-1);
  border-bottom: 1px solid var(--td-border-default);
  padding-bottom: var(--td-space-1);
}

.td-capture-modal__tab {
  background: transparent;
  border: none;
  border-bottom: 2px solid transparent;
  padding: var(--td-space-1) var(--td-space-3);
  cursor: pointer;
  color: var(--td-text-secondary);
  font-size: var(--td-font-sm);
  transition: color 0.15s, border-color 0.15s;
}

.td-capture-modal__tab:hover:not(:disabled) {
  color: var(--td-text-primary);
}

.td-capture-modal__tab--active {
  color: var(--td-text-primary);
  border-bottom-color: var(--td-color-primary);
  font-weight: 600;
}

.td-capture-modal__tab:disabled {
  cursor: not-allowed;
  opacity: 0.6;
}

.td-capture-modal__hint {
  color: var(--td-text-secondary);
  margin: 0;
}

.td-capture-modal__input {
  width: 100%;
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-md);
  padding: var(--td-space-3);
  font-family: inherit;
  font-size: var(--td-font-md);
  line-height: 1.45;
  resize: vertical;
}

.td-capture-modal__input--transcript {
  font-family: var(--td-font-mono, monospace);
  font-size: var(--td-font-sm);
  line-height: 1.55;
}

.td-capture-modal__input:focus {
  outline: 2px solid var(--td-border-focus);
  outline-offset: 1px;
}

.td-capture-modal__file-bar {
  display: flex;
  align-items: center;
  gap: var(--td-space-2);
}

.td-capture-modal__file-input {
  display: none;
}

.td-capture-modal__file-name {
  display: flex;
  align-items: center;
  gap: var(--td-space-1);
  color: var(--td-text-secondary);
  font-size: var(--td-font-sm);
}

.td-capture-modal__file-clear {
  background: transparent;
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-sm);
  color: var(--td-text-secondary);
  cursor: pointer;
  padding: 0 4px;
  font-size: var(--td-font-xs);
  line-height: 1.4;
}

.td-capture-modal__file-clear:disabled {
  cursor: not-allowed;
  opacity: 0.6;
}

.td-capture-modal__char-count {
  margin: 0;
  text-align: right;
  font-size: var(--td-font-xs);
  color: var(--td-text-tertiary);
}

.td-capture-modal__char-count--warn {
  color: var(--td-color-warning, #b45309);
  font-weight: 600;
}

.td-capture-modal__actions {
  display: flex;
  justify-content: flex-end;
  gap: var(--td-space-2);
}

.td-btn {
  border-radius: var(--td-radius-md);
  border: 1px solid transparent;
  padding: var(--td-space-2) var(--td-space-3);
  cursor: pointer;
}

.td-btn--sm {
  padding: var(--td-space-1) var(--td-space-2);
  font-size: var(--td-font-sm);
}

.td-btn--primary {
  background: var(--td-color-primary);
  color: var(--td-text-inverse);
}

.td-btn--secondary {
  background: var(--td-surface-tertiary);
  color: var(--td-text-primary);
  border-color: var(--td-border-default);
}

.td-btn:disabled {
  cursor: not-allowed;
  opacity: 0.6;
}

.td-alert {
  border-radius: var(--td-radius-md);
  padding: var(--td-space-2) var(--td-space-3);
}

.td-alert--error {
  background: var(--td-color-error-light);
  color: var(--td-color-error);
}

/* --- Mobile: full-screen capture --- */
@media (max-width: 640px) {
  .td-overlay {
    padding: 0;
    align-items: stretch;
  }

  .td-capture-modal {
    width: 100%;
    height: 100%;
    max-height: 100vh;
    max-height: 100dvh; /* iOS dynamic viewport — avoids browser chrome overlap */
    border-radius: 0;
    padding: var(--td-space-4);
    display: flex;
    flex-direction: column;
  }

  .td-capture-modal__header h2 {
    font-size: var(--td-font-lg);
  }

  .td-capture-modal__close {
    min-width: 44px;
    min-height: 44px;
    display: flex;
    align-items: center;
    justify-content: center;
    font-size: var(--td-font-lg);
  }

  .td-capture-modal__input {
    flex: 1;
    font-size: 16px; /* Prevents iOS zoom on focus */
    min-height: 200px;
    resize: none;
  }

  .td-capture-modal__actions {
    flex-direction: column-reverse;
    gap: var(--td-space-3);
  }

  .td-capture-modal__actions .td-btn {
    width: 100%;
    min-height: 48px;
    font-size: var(--td-font-base);
    justify-content: center;
  }

  .td-capture-modal__hint {
    font-size: var(--td-font-sm);
    line-height: 1.5;
  }
}
</style>
