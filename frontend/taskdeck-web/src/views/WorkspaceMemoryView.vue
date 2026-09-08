<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import PaperHLBtn from '../components/paper/PaperHLBtn.vue'
import { TdSkeleton } from '../components/ui'
import { useBoardStore } from '../store/boardStore'
import { workspaceInsightsApi } from '../api/workspaceInsights'
import type { Board } from '../types/board'
import type { Memory, MemoryStatus } from '../types/workspaceInsights'

const route = useRoute()
const boardStore = useBoardStore()

const boards = ref<Board[]>([])
const selectedBoardId = ref('')
const boardLoading = ref(false)
const boardError = ref<string | null>(null)
const memories = ref<Memory[]>([])
const loading = ref(false)
const error = ref<string | null>(null)
const showArchived = ref(false)
const initialized = ref(false)
let memoryRequestGeneration = 0
const showEditor = ref(false)
const editingId = ref<string | null>(null)
const formTitle = ref('')
const formText = ref('')
const formStatus = ref<MemoryStatus>('statement')
const formError = ref<string | null>(null)
const saving = ref(false)
const busyMemoryIds = ref(new Set<string>())
const memoryErrors = ref<Record<string, string>>({})

const selectedBoard = computed(() => boards.value.find((board) => board.id === selectedBoardId.value) ?? null)
const editorHeading = computed(() => editingId.value ? 'Correct memory' : 'Add a memory')

function queryBoardId(): string | null {
  const value = route.query.boardId
  if (Array.isArray(value)) return value[0] ?? null
  return typeof value === 'string' ? value : null
}

function errorMessage(value: unknown, fallback: string): string {
  return value instanceof Error && value.message ? value.message : fallback
}

function boardHref(boardId: string): string {
  return `/workspace/boards/${encodeURIComponent(boardId)}`
}

function formatDate(value: string): string {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(date)
}

function statusLabel(status: MemoryStatus): string {
  if (status === 'needsReview') return 'Needs review'
  return status.charAt(0).toUpperCase() + status.slice(1)
}

function setBusy(id: string, value: boolean) {
  const next = new Set(busyMemoryIds.value)
  if (value) next.add(id)
  else next.delete(id)
  busyMemoryIds.value = next
}

function isBusy(id: string): boolean {
  return busyMemoryIds.value.has(id)
}

function replaceMemory(updated: Memory) {
  const index = memories.value.findIndex((memory) => memory.id === updated.id)
  if (index === -1) memories.value.unshift(updated)
  else memories.value.splice(index, 1, updated)
}

async function loadBoards() {
  boardLoading.value = true
  boardError.value = null
  try {
    await boardStore.fetchBoards()
    boards.value = boardStore.boards.filter((board) => !board.isArchived)
    const requested = queryBoardId()
    selectedBoardId.value = boards.value.some((board) => board.id === requested)
      ? requested!
      : boards.value[0]?.id ?? ''
  } catch (value: unknown) {
    boardError.value = errorMessage(value, 'Unable to load your boards.')
  } finally {
    boardLoading.value = false
  }
}

async function loadMemories() {
  if (!selectedBoardId.value) {
    memories.value = []
    return
  }

  const boardId = selectedBoardId.value
  const generation = ++memoryRequestGeneration
  loading.value = true
  error.value = null
  memories.value = []
  try {
    const result = await workspaceInsightsApi.getMemories(boardId, showArchived.value)
    if (generation === memoryRequestGeneration && selectedBoardId.value === boardId) {
      memories.value = result
    }
  } catch (value: unknown) {
    if (generation === memoryRequestGeneration && selectedBoardId.value === boardId) {
      error.value = errorMessage(value, 'Unable to load workspace memory.')
    }
  } finally {
    if (generation === memoryRequestGeneration) loading.value = false
  }
}

function openCreate() {
  editingId.value = null
  formTitle.value = ''
  formText.value = ''
  formStatus.value = 'statement'
  formError.value = null
  showEditor.value = true
}

function openEdit(memory: Memory) {
  editingId.value = memory.id
  formTitle.value = memory.title
  formText.value = memory.text
  formStatus.value = memory.status
  formError.value = null
  showEditor.value = true
}

function closeEditor() {
  showEditor.value = false
  editingId.value = null
  formError.value = null
}

async function saveMemory() {
  const title = formTitle.value.trim()
  const text = formText.value.trim()
  if (!selectedBoardId.value || !title || !text || saving.value) return

  saving.value = true
  formError.value = null
  try {
    if (editingId.value) {
      const current = memories.value.find((memory) => memory.id === editingId.value)
      if (!current) {
        formError.value = 'This memory is no longer available. Refresh and try again.'
        return
      }
      replaceMemory(await workspaceInsightsApi.updateMemory(current.id, {
        title: formTitle.value,
        text: formText.value,
        status: formStatus.value,
        revision: current.revision,
      }))
    } else {
      replaceMemory(await workspaceInsightsApi.createMemory({
        boardId: selectedBoardId.value,
        title: formTitle.value,
        text: formText.value,
        status: formStatus.value,
      }))
    }
    closeEditor()
  } catch (value: unknown) {
    formError.value = errorMessage(value, 'Unable to save this memory.')
  } finally {
    saving.value = false
  }
}

async function toggleArchived(memory: Memory) {
  if (isBusy(memory.id)) return
  const boardId = selectedBoardId.value
  setBusy(memory.id, true)
  memoryErrors.value = { ...memoryErrors.value, [memory.id]: '' }
  try {
    const updated = await workspaceInsightsApi.setMemoryArchived(memory.id, {
      archived: !memory.archived,
      revision: memory.revision,
    })
    if (selectedBoardId.value !== boardId) return
    if (updated.archived === showArchived.value) replaceMemory(updated)
    else memories.value = memories.value.filter((item) => item.id !== updated.id)
  } catch (value: unknown) {
    memoryErrors.value = {
      ...memoryErrors.value,
      [memory.id]: errorMessage(value, 'Unable to update archive status.'),
    }
  } finally {
    setBusy(memory.id, false)
  }
}

function retry() {
  if (selectedBoardId.value) void loadMemories()
  else void loadBoards()
}

onMounted(async () => {
  await loadBoards()
  initialized.value = true
  if (selectedBoardId.value) await loadMemories()
})

watch([selectedBoardId, showArchived], ([nextBoard, nextArchived], [previousBoard, previousArchived]) => {
  if (!initialized.value) return
  if (nextBoard !== previousBoard || nextArchived !== previousArchived) void loadMemories()
})
</script>

<template>
  <main class="paper-memory" aria-labelledby="workspace-memory-title">
    <header class="paper-memory__hero">
      <div>
        <p class="paper-memory__eyebrow">Private workspace</p>
        <h1 id="workspace-memory-title">Workspace memory</h1>
        <p class="paper-memory__lede">
          Keep durable context close to the work. Correct it as your understanding changes, with each revision visible.
        </p>
      </div>
      <div class="paper-memory__hero-mark" aria-hidden="true">▤</div>
    </header>

    <section class="paper-memory__panel paper-memory__controls" aria-label="Memory controls">
      <label class="paper-memory__field" for="memory-board-select">
        <span class="paper-memory__label">Board</span>
        <select id="memory-board-select" v-model="selectedBoardId" :disabled="boardLoading || boards.length === 0 || showEditor || saving">
          <option value="" disabled>Select a board</option>
          <option v-for="board in boards" :key="board.id" :value="board.id">{{ board.name }}</option>
        </select>
      </label>
      <label class="paper-memory__check">
        <input v-model="showArchived" type="checkbox" :disabled="showEditor || saving" />
        <span>Show archived</span>
      </label>
      <span v-if="selectedBoard" class="paper-memory__selected-board">{{ selectedBoard.name }}</span>
      <PaperHLBtn data-action="new-memory" variant="ember" :disabled="!selectedBoardId || showEditor || saving" @click="openCreate">
        Add memory
      </PaperHLBtn>
    </section>

    <p class="paper-memory__trust-note">
      Memories are private to your workspace. Editing or archiving one never changes board cards, columns, or statuses.
    </p>

    <section v-if="showEditor" class="paper-memory__panel paper-memory__editor" aria-labelledby="memory-editor-title">
      <div class="paper-memory__editor-header">
        <div>
          <p class="paper-memory__eyebrow">{{ editingId ? 'Revision' : 'New entry' }}</p>
          <h2 id="memory-editor-title">{{ editorHeading }}</h2>
        </div>
        <PaperHLBtn variant="ghost" :disabled="saving" @click="closeEditor">Close</PaperHLBtn>
      </div>
      <form @submit.prevent="saveMemory">
        <label class="paper-memory__form-field" for="memory-title">
          <span class="paper-memory__label">Title</span>
          <input id="memory-title" v-model="formTitle" type="text" maxlength="240" placeholder="A useful piece of context" />
        </label>
        <label class="paper-memory__form-field" for="memory-text">
          <span class="paper-memory__label">Memory</span>
          <textarea id="memory-text" v-model="formText" rows="5" maxlength="8000" placeholder="Write the context you want to keep…" />
        </label>
        <label class="paper-memory__form-field" for="memory-status">
          <span class="paper-memory__label">Status</span>
          <select id="memory-status" v-model="formStatus">
            <option value="statement">Statement</option>
            <option value="assumption">Assumption</option>
            <option value="unknown">Unknown</option>
            <option value="needsReview">Needs review</option>
          </select>
        </label>
        <div class="paper-memory__form-actions">
          <PaperHLBtn type="submit" variant="ember" :disabled="saving || !formTitle.trim() || !formText.trim()">
            {{ saving ? 'Saving…' : editingId ? 'Save correction' : 'Save memory' }}
          </PaperHLBtn>
          <PaperHLBtn type="button" variant="ghost" :disabled="saving" @click="closeEditor">Cancel</PaperHLBtn>
          <span class="paper-memory__form-note">Private context only; the board stays unchanged.</span>
        </div>
        <p v-if="formError" class="paper-memory__error" role="alert">{{ formError }}</p>
      </form>
    </section>

    <section v-if="boardLoading" class="paper-memory__state" role="status" aria-live="polite">
      <span class="sr-only">Loading boards…</span>
      <TdSkeleton v-for="n in 3" :key="n" height="14px" :width="`${55 + n * 10}%`" />
    </section>

    <section v-else-if="boardError" class="paper-memory__state paper-memory__state--error" role="alert">
      <p>{{ boardError }}</p>
      <PaperHLBtn variant="ghost" @click="retry">Retry</PaperHLBtn>
    </section>

    <section v-else-if="boards.length === 0" class="paper-memory__state" role="status">
      <span class="paper-memory__state-icon" aria-hidden="true">◇</span>
      <h2>No boards yet</h2>
      <p>Memory is attached to board context. Create a board before saving your first entry.</p>
      <RouterLink class="paper-memory__link" to="/workspace/boards">Open boards</RouterLink>
    </section>

    <section v-else-if="loading" class="paper-memory__list" role="status" aria-live="polite">
      <span class="sr-only">Loading workspace memory…</span>
      <article v-for="n in 3" :key="n" class="paper-memory__card paper-memory__card--skeleton">
        <TdSkeleton width="34%" height="11px" />
        <TdSkeleton width="70%" height="20px" />
        <TdSkeleton width="100%" height="12px" />
        <TdSkeleton width="86%" height="12px" />
      </article>
    </section>

    <section v-else-if="error" class="paper-memory__state paper-memory__state--error" role="alert">
      <p>{{ error }}</p>
      <PaperHLBtn variant="ghost" @click="retry">Retry</PaperHLBtn>
    </section>

    <section v-else-if="memories.length === 0" class="paper-memory__state" role="status">
      <span class="paper-memory__state-icon" aria-hidden="true">✦</span>
      <h2>{{ showArchived ? 'No archived memory' : 'No memory yet' }}</h2>
      <p>{{ showArchived ? 'Archived entries for this board will appear here.' : 'Save the context you want to carry forward, then refine it as you learn.' }}</p>
      <PaperHLBtn v-if="!showArchived" variant="primary" :disabled="showEditor || saving" @click="openCreate">Add the first memory</PaperHLBtn>
    </section>

    <section v-else class="paper-memory__list" aria-label="Workspace memory entries">
      <article v-for="memory in memories" :key="memory.id" class="paper-memory__card" :class="{ 'paper-memory__card--archived': memory.archived }">
        <div class="paper-memory__card-topline">
          <span class="paper-memory__status" :class="`paper-memory__status--${memory.status}`">{{ statusLabel(memory.status) }}</span>
          <span v-if="memory.archived" class="paper-memory__archived">Archived</span>
          <span class="paper-memory__revision">Revision {{ memory.revision }}</span>
        </div>
        <h2>{{ memory.title }}</h2>
        <p class="paper-memory__text">{{ memory.text }}</p>
        <div v-if="memory.text !== memory.originalText" class="paper-memory__original">
          <span class="paper-memory__label">Original text</span>
          <p>{{ memory.originalText }}</p>
        </div>
        <div v-if="memory.originalEvidence" class="paper-memory__original">
          <span class="paper-memory__label">Original insight evidence</span>
          <p>{{ memory.originalEvidence }}</p>
        </div>
        <div class="paper-memory__card-footer">
          <RouterLink class="paper-memory__link" :to="boardHref(memory.boardId)">Open board</RouterLink>
          <span class="paper-memory__created">Saved {{ formatDate(memory.createdAt) }}</span>
        </div>
        <details v-if="memory.history.length" class="paper-memory__history">
          <summary>Revision history ({{ memory.history.length }})</summary>
          <ol>
            <li v-for="entry in memory.history" :key="`${memory.id}-${entry.revision}`">
              <div class="paper-memory__history-meta">
                <strong>Revision {{ entry.revision }}</strong>
                <span>{{ statusLabel(entry.status) }}</span>
                <time :datetime="entry.recordedAt">{{ formatDate(entry.recordedAt) }}</time>
              </div>
              <strong>{{ entry.title }}</strong>
              <p>{{ entry.text }}</p>
            </li>
          </ol>
        </details>
        <div class="paper-memory__actions">
          <PaperHLBtn data-action="edit-memory" variant="ghost" :disabled="isBusy(memory.id) || showEditor || saving" @click="openEdit(memory)">Correct</PaperHLBtn>
          <PaperHLBtn data-action="toggle-memory-archive" variant="ghost" :disabled="isBusy(memory.id) || showEditor || saving" @click="toggleArchived(memory)">
            {{ memory.archived ? 'Restore' : 'Archive' }}
          </PaperHLBtn>
        </div>
        <p v-if="memoryErrors[memory.id]" class="paper-memory__error" role="alert">{{ memoryErrors[memory.id] }}</p>
      </article>
    </section>
  </main>
</template>

<style scoped>
.paper-memory {
  min-height: 100%;
  max-width: 1000px;
  margin: 0 auto;
  padding: var(--td-space-8, 1.75rem);
  color: var(--td-text-primary, #e5e2e1);
  background: var(--td-surface-base, #131313);
}

.paper-memory__hero,
.paper-memory__controls,
.paper-memory__editor-header,
.paper-memory__card-topline,
.paper-memory__card-footer,
.paper-memory__actions,
.paper-memory__form-actions,
.paper-memory__history-meta {
  display: flex;
  align-items: center;
}

.paper-memory__hero {
  justify-content: space-between;
  gap: var(--td-space-6, 1.3rem);
  margin-bottom: var(--td-space-6, 1.3rem);
}

.paper-memory__eyebrow,
.paper-memory__label,
.paper-memory__revision,
.paper-memory__archived,
.paper-memory__created,
.paper-memory__status {
  font-size: var(--td-font-xs, 0.6875rem);
  letter-spacing: 0.12em;
  text-transform: uppercase;
}

.paper-memory__eyebrow {
  margin: 0 0 var(--td-space-2, 0.4rem);
  color: var(--td-text-ember, #ff4d4d);
}

.paper-memory h1,
.paper-memory h2,
.paper-memory p {
  margin-top: 0;
}

.paper-memory h1 {
  margin-bottom: var(--td-space-2, 0.4rem);
  font-size: clamp(1.9rem, 4vw, 3rem);
  letter-spacing: -0.03em;
}

.paper-memory__lede {
  max-width: 680px;
  margin-bottom: 0;
  color: var(--td-text-secondary, #e4beba);
  font-size: var(--td-font-lg, 1rem);
  line-height: 1.55;
}

.paper-memory__hero-mark {
  color: var(--td-color-ember, #ff4d4d);
  font-size: 3.5rem;
  line-height: 1;
}

.paper-memory__panel,
.paper-memory__card,
.paper-memory__state {
  border: 1px solid var(--td-border-default, rgba(91, 64, 62, 0.25));
  border-radius: var(--td-radius-lg, 0.5rem);
  background: var(--td-surface-container-low, #1c1b1b);
  box-shadow: var(--td-shadow-sm, 0 2px 8px rgba(0, 0, 0, 0.3));
}

.paper-memory__controls {
  flex-wrap: wrap;
  gap: var(--td-space-4, 0.8rem);
  padding: var(--td-space-4, 0.8rem);
}

.paper-memory__field,
.paper-memory__form-field {
  display: grid;
  gap: var(--td-space-2, 0.4rem);
}

.paper-memory__field {
  min-width: 220px;
}

.paper-memory__label {
  color: var(--td-text-muted, rgba(229, 226, 225, 0.6));
  font-weight: 650;
}

.paper-memory select,
.paper-memory input[type='text'],
.paper-memory textarea {
  border: 1px solid var(--td-border-default, rgba(91, 64, 62, 0.25));
  border-radius: var(--td-radius-md, 0.25rem);
  background: var(--td-surface-container, #201f1f);
  color: var(--td-text-primary, #e5e2e1);
  font: inherit;
}

.paper-memory select,
.paper-memory input[type='text'] {
  min-height: 2.5rem;
  padding: 0 var(--td-space-3, 0.6rem);
}

.paper-memory textarea {
  width: 100%;
  padding: var(--td-space-3, 0.6rem);
  resize: vertical;
}

.paper-memory select:focus-visible,
.paper-memory input:focus-visible,
.paper-memory textarea:focus-visible,
.paper-memory__link:focus-visible,
.paper-memory__check input:focus-visible {
  outline: none;
  box-shadow: var(--td-focus-ring, 0 0 0 2px #ff5352);
}

.paper-memory__check {
  display: flex;
  align-items: center;
  gap: var(--td-space-2, 0.4rem);
  color: var(--td-text-secondary, #e4beba);
  font-size: var(--td-font-sm, 0.75rem);
}

.paper-memory__check input {
  accent-color: var(--td-color-ember, #ff4d4d);
}

.paper-memory__selected-board {
  flex: 1;
  min-width: 160px;
  color: var(--td-text-muted, rgba(229, 226, 225, 0.6));
  font-size: var(--td-font-sm, 0.75rem);
}

.paper-memory__trust-note,
.paper-memory__form-note {
  color: var(--td-text-muted, rgba(229, 226, 225, 0.6));
  font-size: var(--td-font-sm, 0.75rem);
}

.paper-memory__trust-note {
  margin: var(--td-space-3, 0.6rem) 0 var(--td-space-6, 1.3rem);
}

.paper-memory__editor {
  margin-bottom: var(--td-space-6, 1.3rem);
  padding: var(--td-space-5, 1.1rem);
}

.paper-memory__editor-header {
  justify-content: space-between;
  gap: var(--td-space-4, 0.8rem);
  margin-bottom: var(--td-space-5, 1.1rem);
}

.paper-memory__editor-header h2 {
  margin-bottom: 0;
  font-size: var(--td-font-xl, 1.25rem);
}

.paper-memory__editor form {
  display: grid;
  gap: var(--td-space-4, 0.8rem);
}

.paper-memory__form-actions {
  flex-wrap: wrap;
  gap: var(--td-space-2, 0.4rem);
}

.paper-memory__form-note {
  margin-left: auto;
}

.paper-memory__state {
  display: grid;
  justify-items: center;
  gap: var(--td-space-3, 0.6rem);
  padding: var(--td-space-10, 2.25rem) var(--td-space-6, 1.3rem);
  text-align: center;
}

.paper-memory__state p {
  max-width: 520px;
  margin-bottom: var(--td-space-2, 0.4rem);
  color: var(--td-text-secondary, #e4beba);
}

.paper-memory__state h2 {
  margin-bottom: 0;
  font-size: var(--td-font-xl, 1.25rem);
}

.paper-memory__state-icon {
  color: var(--td-color-ember, #ff4d4d);
  font-size: 2rem;
}

.paper-memory__state--error {
  border-color: var(--td-color-error, #ff4d4d);
  background: var(--td-color-error-light, rgba(255, 77, 77, 0.15));
}

.paper-memory__state--error p,
.paper-memory__error {
  color: var(--td-color-error, #ff4d4d);
}

.paper-memory__list {
  display: grid;
  gap: var(--td-space-5, 1.1rem);
}

.paper-memory__card {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-4, 0.8rem);
  padding: var(--td-space-5, 1.1rem);
  border-left: 3px solid var(--td-color-primary, #ffb3ae);
}

.paper-memory__card--archived {
  border-left-color: var(--td-text-tertiary, rgba(229, 226, 225, 0.4));
  opacity: 0.78;
}

.paper-memory__card-topline {
  gap: var(--td-space-3, 0.6rem);
}

.paper-memory__status {
  padding: 0.25rem 0.45rem;
  border: 1px solid currentColor;
  border-radius: var(--td-radius-sm, 0.125rem);
  color: var(--td-color-primary, #ffb3ae);
  white-space: nowrap;
}

.paper-memory__status--assumption,
.paper-memory__status--needsReview {
  color: var(--td-color-warning, #fbbf24);
}

.paper-memory__status--unknown {
  color: var(--td-text-muted, rgba(229, 226, 225, 0.6));
}

.paper-memory__status--statement {
  color: var(--td-color-success, #4ade80);
}

.paper-memory__archived,
.paper-memory__revision,
.paper-memory__created {
  color: var(--td-text-tertiary, rgba(229, 226, 225, 0.4));
  letter-spacing: 0.04em;
  text-transform: none;
}

.paper-memory__revision {
  margin-left: auto;
}

.paper-memory__card h2 {
  margin-bottom: 0;
  font-size: var(--td-font-xl, 1.25rem);
  line-height: 1.25;
}

.paper-memory__text,
.paper-memory__original p,
.paper-memory__history p {
  margin-bottom: 0;
  color: var(--td-text-secondary, #e4beba);
  line-height: 1.55;
  white-space: pre-wrap;
}

.paper-memory__original {
  display: grid;
  gap: var(--td-space-2, 0.4rem);
  padding: var(--td-space-3, 0.6rem);
  border-radius: var(--td-radius-md, 0.25rem);
  background: var(--td-surface-container, #201f1f);
}

.paper-memory__card-footer {
  gap: var(--td-space-3, 0.6rem);
  padding-top: var(--td-space-2, 0.4rem);
  border-top: 1px solid var(--td-border-ghost, rgba(91, 64, 62, 0.15));
}

.paper-memory__created {
  margin-left: auto;
}

.paper-memory__link {
  color: var(--td-color-primary, #ffb3ae);
  font-size: var(--td-font-sm, 0.75rem);
  font-weight: 650;
  text-decoration: none;
}

.paper-memory__link:hover {
  color: var(--td-color-primary-hover, #ff5352);
  text-decoration: underline;
}

.paper-memory__history {
  border-top: 1px solid var(--td-border-ghost, rgba(91, 64, 62, 0.15));
  color: var(--td-text-secondary, #e4beba);
}

.paper-memory__history summary {
  padding: var(--td-space-3, 0.6rem) 0;
  cursor: pointer;
  color: var(--td-text-primary, #e5e2e1);
  font-size: var(--td-font-sm, 0.75rem);
  font-weight: 650;
}

.paper-memory__history ol {
  display: grid;
  gap: var(--td-space-4, 0.8rem);
  margin: 0;
  padding: 0 0 0 var(--td-space-5, 1.1rem);
}

.paper-memory__history li {
  padding-left: var(--td-space-2, 0.4rem);
}

.paper-memory__history-meta {
  flex-wrap: wrap;
  gap: var(--td-space-2, 0.4rem);
  margin-bottom: var(--td-space-2, 0.4rem);
  color: var(--td-text-muted, rgba(229, 226, 225, 0.6));
  font-size: var(--td-font-sm, 0.75rem);
}

.paper-memory__history-meta time {
  margin-left: auto;
}

.paper-memory__history li > strong {
  font-size: var(--td-font-sm, 0.75rem);
}

.paper-memory__actions {
  flex-wrap: wrap;
  gap: var(--td-space-2, 0.4rem);
}

.paper-memory__error {
  margin-bottom: 0;
  font-size: var(--td-font-sm, 0.75rem);
}

.paper-memory__card--skeleton {
  align-items: flex-start;
}

@media (max-width: 640px) {
  .paper-memory {
    padding: var(--td-space-4, 0.8rem);
  }

  .paper-memory__hero-mark {
    display: none;
  }

  .paper-memory__controls > .pbtn {
    width: 100%;
  }

  .paper-memory__form-note,
  .paper-memory__created {
    width: 100%;
    margin-left: 0;
  }
}
</style>
