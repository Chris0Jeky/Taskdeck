<script setup lang="ts">
import { nextTick, onMounted, onUnmounted, ref } from 'vue'
import { useCaptureStore } from '../../store/captureStore'
import { registerEscapeHandler } from '../../composables/useEscapeStack'
import { usePerformanceMark } from '../../composables/usePerformanceMark'

const props = defineProps<{
  boardId?: string | null
  boardName?: string | null
}>()

const emit = defineEmits<{
  close: []
  created: [itemId: string]
}>()

const captureStore = useCaptureStore()

const text = ref('')
const saving = ref(false)
const inlineError = ref<string | null>(null)
const textInput = ref<HTMLTextAreaElement | null>(null)
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

async function submit() {
  if (saving.value) {
    return
  }

  const normalizedText = text.value.trim()
  if (!normalizedText) {
    inlineError.value = 'Capture text is required.'
    return
  }

  try {
    saving.value = true
    inlineError.value = null
    const created = await captureStore.createItem({
      boardId: props.boardId ?? null,
      text: normalizedText,
      source: 'Typed',
    })
    text.value = ''
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
  <div class="td-overlay" role="dialog" aria-label="Capture item" aria-modal="true" @click.self="requestClose">
    <div class="td-capture-modal" @keydown="handleKeydown">
      <header class="td-capture-modal__header">
        <h2>Quick Capture</h2>
        <button class="td-capture-modal__close" aria-label="Close capture modal" @click="requestClose">
          X
        </button>
      </header>

      <p class="td-capture-modal__hint">
        Write or paste anything. Press Ctrl/Cmd+Enter to save.
        <span v-if="props.boardName">This capture will stay linked to {{ props.boardName }}.</span>
      </p>

      <textarea
        ref="textInput"
        v-model="text"
        class="td-capture-modal__input"
        placeholder="Capture a thought, task, or follow-up..."
        rows="8"
      />

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

.td-capture-modal__input:focus {
  outline: 2px solid var(--td-border-focus);
  outline-offset: 1px;
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

/* ─── Mobile: full-screen capture ─── */
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
