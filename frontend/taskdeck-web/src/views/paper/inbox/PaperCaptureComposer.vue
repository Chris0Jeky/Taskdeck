<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from 'vue'
import { useBoardStore } from '../../../store/boardStore'
import PaperHLBtn from '../../../components/paper/PaperHLBtn.vue'
import PaperTagstamp from '../../../components/paper/PaperTagstamp.vue'

/**
 * PaperCaptureComposer — variant B of the Paper Inbox capture surface.
 *
 * A multi-line ledger composer sitting on a paper-card with a metadata
 * sidebar (board picker, label multi-select, optional due date, attachment
 * drop zone).  Cmd/Ctrl+Enter submits.  Attachments are surfaced via an
 * `attachments-changed` event; we don't upload them yet — the parent can
 * decide what to do once the upload pipeline lands.
 */
const props = defineProps<{
  /** Optional board id to default the picker to. */
  defaultBoardId?: string | null
  submitting?: boolean
}>()

const emit = defineEmits<{
  (event: 'submit', payload: {
    text: string
    boardId: string | null
    labels: string[]
    dueAt: string | null
  }): void
  (event: 'attachments-changed', files: File[]): void
}>()

const boardStore = useBoardStore()

const body = ref('')
const boardId = ref<string | null>(props.defaultBoardId ?? null)
const labelInput = ref('')
const labels = ref<string[]>([])
const dueAt = ref<string>('')
const attachments = ref<File[]>([])

const bodyRef = ref<HTMLTextAreaElement | null>(null)
const fileInputRef = ref<HTMLInputElement | null>(null)
const dropActive = ref(false)

const canSubmit = computed(() => body.value.trim().length > 0 && !props.submitting)

function onBodyKeydown(event: KeyboardEvent) {
  if ((event.metaKey || event.ctrlKey) && event.key === 'Enter') {
    event.preventDefault()
    submit()
  }
}

function addLabel() {
  const next = labelInput.value.trim()
  if (!next) return
  if (!labels.value.includes(next)) {
    labels.value.push(next)
  }
  labelInput.value = ''
}

function onLabelKeydown(event: KeyboardEvent) {
  if (event.isComposing) {
    return
  }
  if (event.key === 'Enter' || event.key === ',') {
    event.preventDefault()
    addLabel()
  }
}

function removeLabel(label: string) {
  labels.value = labels.value.filter((l) => l !== label)
}

function onFilesChosen(event: Event) {
  const files = (event.target as HTMLInputElement).files
  if (!files) return
  appendFiles(Array.from(files))
}

function onDrop(event: DragEvent) {
  dropActive.value = false
  const files = event.dataTransfer?.files
  if (!files) return
  appendFiles(Array.from(files))
}

function appendFiles(next: File[]) {
  if (next.length === 0) return
  attachments.value = [...attachments.value, ...next]
  emit('attachments-changed', attachments.value)
}

function removeAttachment(file: File) {
  attachments.value = attachments.value.filter((f) => f !== file)
  emit('attachments-changed', attachments.value)
}

function submit() {
  if (!canSubmit.value) return
  emit('submit', {
    text: body.value.trim(),
    boardId: boardId.value,
    labels: [...labels.value],
    dueAt: dueAt.value || null,
  })
}

function resetDraft() {
  body.value = ''
  labels.value = []
  dueAt.value = ''
  attachments.value = []
  emit('attachments-changed', attachments.value)
}

onMounted(async () => {
  // Best-effort prime — boards are useful in the picker.  Errors are handled
  // by the store's toast surface; we don't block rendering on it.
  if (boardStore.boards.length === 0) {
    try {
      await boardStore.fetchBoards()
    } catch {
      // store handles toast
    }
  }
  await nextTick()
  bodyRef.value?.focus()
})

defineExpose({ focus: () => bodyRef.value?.focus(), resetDraft })
</script>

<template>
  <section class="paper-composer card-lift">
    <header class="paper-composer__header">
      <PaperTagstamp tone="ember">Capture · Draft</PaperTagstamp>
      <span class="tk-meta paper-composer__meta">local-only · saves to inbox</span>
    </header>

    <div class="paper-composer__body">
      <div class="paper-composer__main">
        <label class="paper-composer__label">
          <span class="tk-eyebrow">Body</span>
          <textarea
            ref="bodyRef"
            v-model="body"
            class="paper-composer__textarea"
            rows="6"
            aria-label="Capture body"
            placeholder="The thought, in plain language…"
            @keydown="onBodyKeydown"
          />
        </label>

        <!-- Attachments / drop zone — hairline only.  Real upload pipeline lands later. -->
        <!-- eslint-disable-next-line vuejs-accessibility/no-static-element-interactions -- drop zone trigger handled via the explicit Browse button below; the surrounding region is visual chrome -->
        <div
          class="paper-composer__drop"
          :class="{ 'paper-composer__drop--active': dropActive }"
          data-testid="paper-composer-drop"
          @dragenter.prevent="dropActive = true"
          @dragover.prevent="dropActive = true"
          @dragleave.prevent="dropActive = false"
          @drop.prevent="onDrop"
        >
          <span class="tk-meta">Drop files here, or</span>
          <button type="button" class="paper-composer__file-trigger" @click="fileInputRef?.click()">
            Browse
          </button>
          <input
            ref="fileInputRef"
            type="file"
            multiple
            class="paper-composer__file-input"
            aria-label="Attach files"
            @change="onFilesChosen"
          />
        </div>

        <ul v-if="attachments.length > 0" class="paper-composer__attachments">
          <li v-for="file in attachments" :key="file.name + ':' + file.size">
            <span class="paper-composer__attachment-name">{{ file.name }}</span>
            <button type="button" class="paper-composer__attachment-remove" @click="removeAttachment(file)">
              Remove
            </button>
          </li>
        </ul>
      </div>

      <aside class="paper-composer__aside">
        <label class="paper-composer__label">
          <span class="tk-eyebrow">Board</span>
          <select v-model="boardId" class="paper-composer__select" aria-label="Board picker">
            <option :value="null">No board · land in inbox</option>
            <option v-for="board in boardStore.boards" :key="board.id" :value="board.id">
              {{ board.name }}
            </option>
          </select>
        </label>

        <label class="paper-composer__label">
          <span class="tk-eyebrow">Labels</span>
          <input
            v-model="labelInput"
            class="paper-composer__input"
            type="text"
            aria-label="Add label"
            placeholder="add and press Enter"
            @keydown="onLabelKeydown"
          />
          <ul v-if="labels.length > 0" class="paper-composer__labels">
            <li v-for="label in labels" :key="label">
              <PaperTagstamp tone="ember">{{ label }}</PaperTagstamp>
              <button type="button" class="paper-composer__label-remove" @click="removeLabel(label)">
                ×
              </button>
            </li>
          </ul>
        </label>

        <label class="paper-composer__label">
          <span class="tk-eyebrow">Due (optional)</span>
          <input
            v-model="dueAt"
            class="paper-composer__input"
            type="date"
            aria-label="Due date"
          />
        </label>
      </aside>
    </div>

    <footer class="paper-composer__footer">
      <span class="tk-meta">
        Captures land in <span class="tk-ink-italic">Inbox</span>. Linking to a board creates a proposal, not a card.
      </span>
      <span class="paper-composer__spacer" />
      <PaperHLBtn label="Capture" kbd="⌘⏎" variant="ember" :disabled="!canSubmit" @click="submit" />
    </footer>
  </section>
</template>

<style scoped>
.paper-composer {
  padding: 0;
  overflow: hidden;
}
.paper-composer__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 12px 16px;
  border-bottom: 1px solid var(--line-soft);
  background: var(--paper-2);
}
.paper-composer__meta {
  font-size: 11px;
}
.paper-composer__body {
  display: grid;
  grid-template-columns: 1fr 260px;
  gap: 24px;
  padding: 22px;
}
.paper-composer__main {
  display: flex;
  flex-direction: column;
  gap: 14px;
  min-width: 0;
}
.paper-composer__aside {
  display: flex;
  flex-direction: column;
  gap: 14px;
  border-left: 1px solid var(--line-soft);
  padding-left: 24px;
}
.paper-composer__label {
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.paper-composer__textarea {
  width: 100%;
  min-height: 130px;
  padding: 10px 12px;
  border: 1px solid var(--line-soft);
  border-bottom-color: var(--line);
  border-radius: 2px;
  background: var(--paper);
  font-family: var(--sans);
  font-size: 14.5px;
  color: var(--ink-deep);
  resize: vertical;
  outline: none;
}
.paper-composer__textarea:focus {
  border-color: var(--ember);
}
.paper-composer__input,
.paper-composer__select {
  padding: 8px 10px;
  border: 1px solid var(--line-soft);
  border-bottom-color: var(--line);
  border-radius: 2px;
  background: var(--paper);
  font-family: var(--sans);
  font-size: 13px;
  color: var(--ink);
  outline: none;
}
.paper-composer__input:focus,
.paper-composer__select:focus {
  border-color: var(--ember);
}
.paper-composer__drop {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 12px;
  border: 1px dashed var(--line);
  border-radius: 2px;
  background: var(--paper);
  color: var(--mute);
}
.paper-composer__drop--active {
  border-color: var(--ember);
  color: var(--ember-ink, var(--ember));
}
.paper-composer__file-trigger {
  font-family: var(--mono);
  font-size: 11px;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: var(--ink);
  background: transparent;
  border: 0;
  padding: 0;
  cursor: pointer;
  border-bottom: 1px solid var(--line);
}
.paper-composer__file-input {
  display: none;
}
.paper-composer__attachments {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.paper-composer__attachments li {
  display: flex;
  align-items: center;
  gap: 8px;
  font-family: var(--mono);
  font-size: 11px;
  color: var(--ink-2);
}
.paper-composer__attachment-name {
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.paper-composer__attachment-remove,
.paper-composer__label-remove {
  background: transparent;
  border: 0;
  padding: 0 4px;
  font-family: var(--mono);
  font-size: 11px;
  color: var(--mute);
  cursor: pointer;
}
.paper-composer__labels {
  list-style: none;
  margin: 6px 0 0;
  padding: 0;
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}
.paper-composer__labels li {
  display: inline-flex;
  align-items: center;
  gap: 2px;
}
.paper-composer__footer {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 12px 16px;
  border-top: 1px solid var(--line-soft);
}
.paper-composer__spacer {
  flex: 1;
}
@media (max-width: 900px) {
  .paper-composer__body {
    grid-template-columns: 1fr;
  }
  .paper-composer__aside {
    border-left: 0;
    border-top: 1px solid var(--line-soft);
    padding-left: 0;
    padding-top: 16px;
  }
}
</style>
