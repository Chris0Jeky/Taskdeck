<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import PaperHLBtn from '../../../components/paper/PaperHLBtn.vue'
import PaperTagstamp from '../../../components/paper/PaperTagstamp.vue'
import { useBoardStore } from '../../../store/boardStore'
import { canMutateSelection, sourceLabel, statusLabel } from '../../../components/inbox/inboxUtils'
import type { CaptureItemSummary, CaptureStatusValue } from '../../../types/capture'

/**
 * PaperTriageTable — captured items list rendered in the paper-card ledger
 * style.  Each row surfaces excerpt, source/status as Paper tagstamps, and
 * accept / reject actions as hairline buttons.
 *
 * Idempotent: `actionBusyItemId` mirrors the captureStore field so a
 * double-click on Accept can't fire `accept` twice for the same row.
 *
 * Board-less captures (Home quick-capture) can't be triaged into a proposal
 * without a target board, so Accept first reveals an inline board picker
 * (#1764); the chosen board rides the `accept` event and the server links it.
 */
const props = defineProps<{
  items: CaptureItemSummary[]
  loadingList?: boolean
  listError?: string | null
  actionBusyItemId?: string | null
  triagePollingItemId?: string | null
}>()

const emit = defineEmits<{
  (event: 'accept', itemId: string, boardId?: string | null): void
  (event: 'reject', itemId: string): void
  (event: 'open', itemId: string): void
  (event: 'retry'): void
}>()

const boardStore = useBoardStore()

// Item currently awaiting a board choice before its triage can be accepted.
const boardPickItemId = ref<string | null>(null)
const pickedBoardId = ref<string | null>(null)

const hasItems = computed(() => props.items.length > 0)
const hasMutationInFlight = computed(
  () => props.actionBusyItemId !== null && props.actionBusyItemId !== undefined,
)

function canMutate(item: CaptureItemSummary): boolean {
  return canMutateSelection(item.status)
}

function isActionDisabled(item: CaptureItemSummary): boolean {
  return hasMutationInFlight.value || props.triagePollingItemId === item.id || !canMutate(item)
}

function hasBoard(item: CaptureItemSummary): boolean {
  return typeof item.boardId === 'string' && item.boardId.length > 0
}

function isPickingBoard(item: CaptureItemSummary): boolean {
  return boardPickItemId.value === item.id
}

async function onAccept(item: CaptureItemSummary) {
  if (isActionDisabled(item)) return
  if (hasBoard(item)) {
    emit('accept', item.id, item.boardId)
    return
  }
  // No board yet — require the user to choose one before we queue triage.
  pickedBoardId.value = null
  boardPickItemId.value = item.id
  if (boardStore.boards.length === 0) {
    try {
      await boardStore.fetchBoards()
    } catch {
      // store surfaces its own toast; the picker still renders with whatever loaded.
    }
  }
}

function confirmBoardAndAccept(item: CaptureItemSummary) {
  if (isActionDisabled(item)) return
  if (!pickedBoardId.value) return
  emit('accept', item.id, pickedBoardId.value)
  cancelBoardPick()
}

function cancelBoardPick() {
  boardPickItemId.value = null
  pickedBoardId.value = null
}

function onReject(item: CaptureItemSummary) {
  if (isActionDisabled(item)) return
  emit('reject', item.id)
}

function statusTone(status: CaptureStatusValue): 'ember' | 'applied' | 'overdue' | 'mute' {
  const value = statusLabel(status).toLowerCase()
  if (value.includes('failed') || value.includes('error')) return 'overdue'
  if (value.includes('triag')) return 'ember'
  if (value.includes('proposed') || value.includes('ready')) return 'ember'
  if (value.includes('applied') || value.includes('accept')) return 'applied'
  return 'mute'
}

function failureReason(item: CaptureItemSummary): string | null {
  const message = item.errorMessage
  if (!message) return null
  return statusTone(item.status) === 'overdue' ? message : null
}

onMounted(() => {
  // Prime boards so the picker is ready if the user accepts a board-less capture.
  // Best-effort — the store owns its own error/toast surface.
  if (boardStore.boards.length === 0) {
    void boardStore.fetchBoards().catch(() => undefined)
  }
})

function formatTime(iso: string): string {
  try {
    const d = new Date(iso)
    if (Number.isNaN(d.getTime())) return ''
    return d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
  } catch {
    return ''
  }
}
</script>

<template>
  <section class="paper-triage" aria-label="Captured items">
    <header class="paper-triage__header">
      <h2 class="tk-h3 paper-triage__title">Today's captures</h2>
      <span class="tk-meta">
        {{ hasItems ? `${items.length} item${items.length === 1 ? '' : 's'} · most recent first` : 'No captures yet' }}
      </span>
    </header>

    <div v-if="loadingList && !hasItems" class="paper-triage__empty">
      <span class="tk-meta">Loading…</span>
    </div>

    <div v-else-if="listError" class="paper-triage__empty paper-triage__empty--error" role="alert">
      <p class="tk-body">{{ listError }}</p>
      <button type="button" class="paper-triage__retry" @click="emit('retry')">
        Retry
      </button>
    </div>

    <div v-if="!loadingList && !listError && !hasItems" class="paper-triage__empty">
      <p class="tk-body">A pen and a phrase. Drop a thought above to start.</p>
    </div>

    <ul v-if="hasItems" class="paper-triage__list">
      <li
        v-for="item in items"
        :key="item.id"
        class="paper-triage__row"
        :data-item-id="item.id"
      >
        <button
          type="button"
          class="paper-triage__open"
          :aria-label="`Open capture ${item.id}`"
          @click="emit('open', item.id)"
        >
          <span class="tk-serial paper-triage__time">{{ formatTime(item.createdAt) }}</span>
          <span class="paper-triage__excerpt">{{ item.textExcerpt }}</span>
          <span
            v-if="failureReason(item)"
            class="paper-triage__reason"
            role="alert"
            :title="failureReason(item) ?? ''"
            data-testid="capture-failure-reason"
          >
            {{ failureReason(item) }}
          </span>
        </button>

        <div class="paper-triage__tags">
          <PaperTagstamp :tone="statusTone(item.status)">{{ statusLabel(item.status) }}</PaperTagstamp>
          <PaperTagstamp tone="mute">{{ sourceLabel(item.source) }}</PaperTagstamp>
        </div>

        <div v-if="isPickingBoard(item)" class="paper-triage__board-pick" data-testid="capture-board-pick">
          <label class="paper-triage__board-label">
            <span class="tk-eyebrow">Board</span>
            <select
              v-model="pickedBoardId"
              class="paper-triage__board-select"
              aria-label="Choose a board for this capture"
            >
              <option :value="null" disabled>Select a board…</option>
              <option v-for="board in boardStore.boards" :key="board.id" :value="board.id">
                {{ board.name }}
              </option>
            </select>
          </label>
          <div class="paper-triage__actions">
            <PaperHLBtn
              label="Accept on board"
              variant="ember"
              :disabled="isActionDisabled(item) || !pickedBoardId"
              data-action="accept-on-board"
              @click="confirmBoardAndAccept(item)"
            />
            <PaperHLBtn
              label="Cancel"
              variant="ghost"
              data-action="cancel-board-pick"
              @click="cancelBoardPick"
            />
          </div>
        </div>

        <div v-else class="paper-triage__actions">
          <PaperHLBtn
            label="Accept"
            variant="ember"
            :disabled="isActionDisabled(item)"
            data-action="accept"
            @click="onAccept(item)"
          />
          <PaperHLBtn
            label="Reject"
            variant="ghost"
            :disabled="isActionDisabled(item)"
            data-action="reject"
            @click="onReject(item)"
          />
        </div>
      </li>
    </ul>
  </section>
</template>

<style scoped>
.paper-triage {
  margin-top: 32px;
}
.paper-triage__header {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  margin-bottom: 12px;
}
.paper-triage__title {
  margin: 0;
  font-family: var(--serif);
  font-size: 20px;
  color: var(--ink-deep);
}
.paper-triage__empty {
  padding: 22px;
  border: 1px solid var(--line-soft);
  border-radius: 4px;
  background: var(--paper-card);
  color: var(--mute);
}
.paper-triage__empty--error {
  border-color: var(--overdue);
  color: var(--overdue);
}
.paper-triage__retry {
  margin-top: 10px;
  border: 1px solid var(--line);
  border-radius: 2px;
  background: var(--paper);
  color: var(--ink);
  font-family: var(--mono);
  font-size: 11px;
  letter-spacing: 0.04em;
  padding: 6px 10px;
  text-transform: uppercase;
  cursor: pointer;
}
.paper-triage__list {
  margin: 0;
  padding: 0;
  list-style: none;
  border: 1px solid var(--line-soft);
  border-radius: 4px;
  background: var(--paper-card);
  overflow: hidden;
}
.paper-triage__row {
  display: grid;
  grid-template-columns: 1fr auto auto;
  gap: 16px;
  align-items: center;
  padding: 14px 18px;
  border-bottom: 1px solid var(--line-soft);
}
.paper-triage__row:last-child {
  border-bottom: 0;
}
.paper-triage__open {
  display: grid;
  grid-template-columns: 60px 1fr;
  gap: 12px;
  align-items: baseline;
  text-align: left;
  background: transparent;
  border: 0;
  padding: 0;
  cursor: pointer;
  color: var(--ink);
  min-width: 0;
}
.paper-triage__time {
  color: var(--mute);
  font-size: 11px;
}
.paper-triage__excerpt {
  font-family: var(--sans);
  font-size: 13.5px;
  line-height: 1.5;
  color: var(--ink);
  overflow: hidden;
  text-overflow: ellipsis;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
}
.paper-triage__tags {
  display: flex;
  gap: 6px;
}
.paper-triage__actions {
  display: flex;
  gap: 6px;
}
.paper-triage__reason {
  grid-column: 2;
  margin-top: 4px;
  font-family: var(--sans);
  font-size: 12px;
  line-height: 1.4;
  color: var(--overdue);
  overflow: hidden;
  text-overflow: ellipsis;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
}
.paper-triage__board-pick {
  display: flex;
  align-items: flex-end;
  gap: 10px;
}
.paper-triage__board-label {
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.paper-triage__board-select {
  padding: 6px 8px;
  border: 1px solid var(--line-soft);
  border-bottom-color: var(--line);
  border-radius: 2px;
  background: var(--paper);
  font-family: var(--sans);
  font-size: 13px;
  color: var(--ink);
  outline: none;
}
.paper-triage__board-select:focus {
  border-color: var(--ember);
}
</style>
