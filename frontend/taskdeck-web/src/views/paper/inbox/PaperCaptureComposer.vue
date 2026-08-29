<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useBoardStore } from '../../../store/boardStore'
import PaperHLBtn from '../../../components/paper/PaperHLBtn.vue'
import PaperTagstamp from '../../../components/paper/PaperTagstamp.vue'
import { TdDateField } from '../../../components/ui'
import type { Board } from '../../../types/board'

/**
 * PaperCaptureComposer — variant B of the Paper Inbox capture surface.
 *
 * A multi-line ledger composer sitting on a paper-card with a metadata
 * sidebar (board picker, label multi-select, optional due date). Cmd/Ctrl+Enter submits.
 * Attachments remain visibly unavailable until a persistence lane exists.
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
}>()

const boardStore = useBoardStore()
const { t } = useI18n()

const body = ref('')
const boardId = ref<string | null>(props.defaultBoardId ?? null)
const labelInput = ref('')
const labels = ref<string[]>([])
const dueAt = ref<string>('')

const bodyRef = ref<HTMLTextAreaElement | null>(null)

const inputsDisabled = computed(() => !!props.submitting)

/**
 * Write capability comes from the server (`BoardDto.CanWrite`, #1836). Choosing
 * a read-only board here produces a capture that 403s the moment it is accepted
 * for triage, so such boards are DISABLED and annotated "view-only" rather than
 * hidden — no silent filtering, no reachable 403 from the picker.
 *
 * Only an explicit `false` gates; a payload without the field behaves as before.
 */
function isBoardWritable(board: Board): boolean {
  return board.canWrite !== false
}

function boardOptionLabel(board: Board): string {
  return isBoardWritable(board) ? board.name : t('inbox.boardPicker.viewOnlyOption', { name: board.name })
}

const hasReadOnlyBoard = computed(() => boardStore.boards.some((board) => !isBoardWritable(board)))

const selectedBoardIsWritable = computed(() => {
  if (!boardId.value) return true
  const selected = boardStore.boards.find((board) => board.id === boardId.value)
  // An id outside the loaded list is left alone: the server stays the authority,
  // and this gate exists to stop a KNOWN read-only selection.
  return selected ? isBoardWritable(selected) : true
})

const canSubmit = computed(
  () => body.value.trim().length > 0 && !props.submitting && selectedBoardIsWritable.value,
)

watch(
  () => props.defaultBoardId,
  (next) => {
    boardId.value = next ?? null
  },
)

function onBodyKeydown(event: KeyboardEvent) {
  if (inputsDisabled.value) return
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
  if (inputsDisabled.value) return
  if (event.isComposing) {
    return
  }
  if (event.key === 'Enter' || event.key === ',') {
    event.preventDefault()
    addLabel()
  }
}

function removeLabel(label: string) {
  if (inputsDisabled.value) return
  labels.value = labels.value.filter((l) => l !== label)
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
            :disabled="inputsDisabled"
            @keydown="onBodyKeydown"
          />
        </label>

        <p class="paper-composer__drop tk-meta" data-testid="paper-composer-attachments-unavailable">
          Attachments are not saved with captures yet.
        </p>
      </div>

      <aside class="paper-composer__aside">
        <label class="paper-composer__label">
          <span class="tk-eyebrow">Board</span>
          <select
            v-model="boardId"
            class="paper-composer__select"
            aria-label="Board picker"
            :disabled="inputsDisabled"
          >
            <option :value="null">No board · land in inbox</option>
            <option
              v-for="board in boardStore.boards"
              :key="board.id"
              :value="board.id"
              :disabled="!isBoardWritable(board)"
              :data-writable="isBoardWritable(board)"
            >
              {{ boardOptionLabel(board) }}
            </option>
          </select>
          <span v-if="hasReadOnlyBoard" class="tk-meta" data-testid="composer-view-only-hint">
            {{ t('inbox.boardPicker.viewOnlyHint') }}
          </span>
        </label>

        <label class="paper-composer__label">
          <span class="tk-eyebrow">Labels</span>
          <input
            v-model="labelInput"
            class="paper-composer__input"
            type="text"
            aria-label="Add label"
            placeholder="add and press Enter"
            :disabled="inputsDisabled"
            @keydown="onLabelKeydown"
          />
          <ul v-if="labels.length > 0" class="paper-composer__labels">
            <li v-for="label in labels" :key="label">
              <PaperTagstamp tone="ember">{{ label }}</PaperTagstamp>
              <button
                type="button"
                class="paper-composer__label-remove"
                :disabled="inputsDisabled"
                @click="removeLabel(label)"
              >
                ×
              </button>
            </li>
          </ul>
        </label>

        <label class="paper-composer__label">
          <span class="tk-eyebrow">Due (optional)</span>
          <TdDateField
            v-model="dueAt"
            class="paper-composer__input"
            aria-label="Due date"
            :disabled="inputsDisabled"
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
