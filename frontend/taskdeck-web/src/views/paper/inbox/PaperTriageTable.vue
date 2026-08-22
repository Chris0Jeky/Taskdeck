<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import PaperHLBtn from '../../../components/paper/PaperHLBtn.vue'
import PaperTagstamp from '../../../components/paper/PaperTagstamp.vue'
import PaperTriageRowEdit from './PaperTriageRowEdit.vue'
import { useBoardStore } from '../../../store/boardStore'
import {
  canMutateSelection,
  captureRowState,
  sourceLabel,
  statusLabel,
} from '../../../components/inbox/inboxUtils'
import type { CaptureRowState } from '../../../components/inbox/inboxUtils'
import type { Board } from '../../../types/board'
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
 *
 * A pre-triage row can also have its text CORRECTED before Accept turns it into
 * a proposal (GH-1951) — the Legacy detail panel's `Edit Text` affordance,
 * ported to a skin that has no detail panel. `PaperTriageRowEdit` owns that
 * surface; this table owns only which row is open and the fact that a row with
 * an open editor cannot simultaneously be decided.
 *
 * Every blocked primary action states its reason (#1944). A precondition that
 * is not met disables the button AND renders why — an enabled-looking button
 * that swallows the click is the failure this surface was reported for. The
 * same rule drives the per-row decision line: once accepted or rejected, the
 * row says what happened and where the work went, so a decided row can never
 * render identically to one still waiting on a decision.
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
const { t } = useI18n()

// Item currently awaiting a board choice before its triage can be accepted.
const boardPickItemId = ref<string | null>(null)
const pickedBoardId = ref<string | null>(null)

// Row whose text is open for pre-triage correction (GH-1951). One at a time:
// the editor holds an unsaved draft, and a second open row would give the user
// two drafts and one Save.
const editItemId = ref<string | null>(null)

/**
 * Which action this table started, for which row (#1944).
 *
 * `actionBusyItemId` mirrors the captureStore's single busy slot and is action-
 * AGNOSTIC: `ignoreItem` (Reject) sets it exactly the way `triageItem` (Accept)
 * does, and the store exposes no kind alongside it. Narrating every in-flight
 * row as "Sending to Review…" therefore tells a REJECTED row the opposite of
 * what is happening to it, until the detail refresh lands. The intent is only
 * knowable at the click, so it is recorded here.
 *
 * Scope is deliberately narrow — see `rowState`: it is consulted only while the
 * busy row is still the row it was recorded for, so it can never speak for a
 * mutation some other surface started.
 */
type PendingAction = { itemId: string; kind: 'accept' | 'reject' }
const pendingAction = ref<PendingAction | null>(null)

watch(
  () => props.actionBusyItemId,
  (busyItemId) => {
    // The moment the busy row clears or moves, the remembered intent stops
    // describing anything in flight — keeping it would let a later mutation on
    // the same row inherit a decision the user never made this time. A null or
    // absent busy id falls out of the same comparison: it matches no item id.
    if (busyItemId !== pendingAction.value?.itemId) {
      pendingAction.value = null
    }
  },
)

watch(
  () => props.items,
  (rows) => {
    // A row that has left the list can no longer be edited, and leaving the id
    // set would silently reopen the editor if a row with that id came back.
    if (editItemId.value !== null && !rows.some((row) => row.id === editItemId.value)) {
      editItemId.value = null
    }
  },
)

/**
 * Write capability comes from the server (`BoardDto.CanWrite`, #1836) — a board
 * the user can only read would 403 at accept, so it is shown DISABLED and
 * annotated rather than filtered away: a Viewer should see why a board is
 * unavailable, not wonder where it went.
 *
 * Only an explicit `false` gates. A payload without the field (older cache,
 * a non-caller-scoped source) behaves as it did before the field existed.
 */
function isBoardWritable(board: Board): boolean {
  return board.canWrite !== false
}

function boardOptionLabel(board: Board): string {
  return isBoardWritable(board) ? board.name : t('inbox.boardPicker.viewOnlyOption', { name: board.name })
}

const hasReadOnlyBoard = computed(() => boardStore.boards.some((board) => !isBoardWritable(board)))

const pickedBoardIsWritable = computed(() => {
  if (!pickedBoardId.value) return false
  const picked = boardStore.boards.find((board) => board.id === pickedBoardId.value)
  // An id that is not in the loaded list is left alone: the server remains the
  // authority, and this gate exists to stop a KNOWN read-only pick.
  return picked ? isBoardWritable(picked) : true
})

/**
 * Why the picker's confirm button is off, or `null` when it is live (#1944).
 *
 * The button was already `disabled` in this state, but `.pbtn` had no disabled
 * styling and the row said nothing — so it read as an enabled primary action
 * that silently did nothing. The single source of truth now drives the
 * `disabled` binding, the guard inside the handler, AND the visible reason:
 * they cannot drift apart into "off for a reason nobody stated".
 *
 * Order matters: "nothing picked" is reported before "not writable", because
 * `pickedBoardIsWritable` is also false when nothing is picked.
 */
type BoardPickBlock = 'noBoards' | 'noBoard' | 'viewOnly'

const boardPickBlock = computed<BoardPickBlock | null>(() => {
  if (boardStore.boards.length === 0) return 'noBoards'
  if (!pickedBoardId.value) return 'noBoard'
  if (!pickedBoardIsWritable.value) return 'viewOnly'
  return null
})

const boardPickBlockMessage = computed(() =>
  boardPickBlock.value ? t(`inbox.triage.boardPick.blocked.${boardPickBlock.value}`) : '',
)

function boardPickReasonId(item: CaptureItemSummary): string {
  return `board-pick-reason-${item.id}`
}

const hasItems = computed(() => props.items.length > 0)
const hasMutationInFlight = computed(
  () => props.actionBusyItemId !== null && props.actionBusyItemId !== undefined,
)

function canMutate(item: CaptureItemSummary): boolean {
  return canMutateSelection(item.status)
}

function isEditing(item: CaptureItemSummary): boolean {
  return editItemId.value === item.id
}

/**
 * A SIBLING row, while some other row holds an open editor (GH-1951).
 *
 * `editItemId` is a single slot, so Edit here would move it — unmounting the
 * open editor and taking the draft inside it with no warning and no undo: the
 * same silent loss the editing row is already protected from, one row over.
 *
 * The gate is on the editor being OPEN, not on the draft being dirty. The
 * table cannot see the child's draft without new plumbing, and an open-editor
 * gate is the discipline Accept and Reject already follow on the editing row —
 * guessing "probably not dirty" would be exactly the assumption that loses the
 * text on the one occasion it is wrong.
 */
function isEditingElsewhere(item: CaptureItemSummary): boolean {
  return editItemId.value !== null && editItemId.value !== item.id
}

function editorOpenReasonId(item: CaptureItemSummary): string {
  return `capture-editor-open-reason-${item.id}`
}

/**
 * A row with an open editor is deliberately undecidable (GH-1951).
 *
 * Accept while an unsaved draft sits in the textarea would triage the text the
 * user is in the middle of replacing and discard the correction without a word
 * — the exact silent-loss failure this surface keeps being reported for. The
 * row states the reason next to the editor rather than just going grey.
 *
 * The open editor freezes the OTHER rows too, for the same reason and with its
 * own visible reason next to each of them (GH-1944): every decision on this
 * surface either replaces the draft's row or moves the editor off it.
 */
function isActionDisabled(item: CaptureItemSummary): boolean {
  return hasMutationInFlight.value ||
    props.triagePollingItemId === item.id ||
    !canMutate(item) ||
    isEditing(item) ||
    isEditingElsewhere(item)
}

function onEdit(item: CaptureItemSummary) {
  if (isActionDisabled(item)) return
  // Opening the editor cancels a board pick in progress: they compete for the
  // same row and the same decision, and leaving both open would let a stale
  // pick confirm against text that is being rewritten.
  cancelBoardPick()
  editItemId.value = item.id
}

function closeEdit() {
  editItemId.value = null
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
    pendingAction.value = { itemId: item.id, kind: 'accept' }
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
  // Belt and braces behind the disabled button: never emit an accept with no
  // board, nor one the server would answer with a 403. Every branch that stops
  // the emit also renders its reason above the button (`boardPickBlock`).
  if (boardPickBlock.value !== null) return
  pendingAction.value = { itemId: item.id, kind: 'accept' }
  emit('accept', item.id, pickedBoardId.value)
  cancelBoardPick()
}

function cancelBoardPick() {
  boardPickItemId.value = null
  pickedBoardId.value = null
}

function onReject(item: CaptureItemSummary) {
  if (isActionDisabled(item)) return
  pendingAction.value = { itemId: item.id, kind: 'reject' }
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

/**
 * States a row can narrate: the server-derived ones plus `rejecting`, which no
 * capture status produces — a reject is only observable here, between the click
 * and the refresh that turns the row into `rejected`.
 */
type TriageRowState = CaptureRowState | 'rejecting'

/**
 * The row's decision state (#1944). A mutation THIS table started for THIS row
 * reads as its own intent even before the server status catches up, so the
 * click has a visible consequence immediately rather than after the next poll.
 *
 * The intent gate is the whole point: `actionBusyItemId` alone cannot tell an
 * accept from a reject, and guessing `sending` narrates a rejection as a trip
 * to Review. With no recorded intent — a busy flag another surface set, a row
 * mounted mid-flight — the server status answers instead. Saying less is the
 * honest failure mode; the row simply stays quiet until the refresh lands.
 */
function rowState(item: CaptureItemSummary): TriageRowState {
  const pending = pendingAction.value
  if (props.actionBusyItemId === item.id && pending?.itemId === item.id) {
    return pending.kind === 'reject' ? 'rejecting' : 'sending'
  }
  return captureRowState(item.status)
}

/**
 * What the user's decision did and what happens next. `undecided` (and an
 * out-of-contract `unknown`) return null: silence is correct only while the
 * row is genuinely still waiting on the user.
 */
function decisionLine(item: CaptureItemSummary): string | null {
  const state = rowState(item)
  if (state === 'undecided' || state === 'unknown') return null
  return t(`inbox.triage.decision.${state}`)
}

function stateTagTitle(item: CaptureItemSummary): string {
  return t('inbox.triage.tag.state', { label: statusLabel(item.status) })
}

function sourceTagTitle(item: CaptureItemSummary): string {
  return t('inbox.triage.tag.source', { label: sourceLabel(item.source) })
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
        :data-row-state="rowState(item)"
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
          <PaperTagstamp
            :tone="statusTone(item.status)"
            data-tag-kind="state"
            :title="stateTagTitle(item)"
          >{{ statusLabel(item.status) }}</PaperTagstamp>
          <PaperTagstamp
            tone="mute"
            class="paper-triage__tag--source"
            data-tag-kind="source"
            :title="sourceTagTitle(item)"
          >{{ sourceLabel(item.source) }}</PaperTagstamp>
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
          </label>
          <p v-if="hasReadOnlyBoard" class="paper-triage__board-hint" data-testid="board-pick-view-only-hint">
            {{ t('inbox.boardPicker.viewOnlyHint') }}
          </p>
          <p
            v-if="boardPickBlock"
            :id="boardPickReasonId(item)"
            class="paper-triage__board-reason"
            role="status"
            data-testid="board-pick-reason"
            :data-reason="boardPickBlock"
          >
            {{ boardPickBlockMessage }}
          </p>
          <div class="paper-triage__actions">
            <PaperHLBtn
              label="Accept on board"
              variant="ember"
              :disabled="isActionDisabled(item) || boardPickBlock !== null"
              :aria-describedby="boardPickBlock ? boardPickReasonId(item) : undefined"
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
            :aria-describedby="isEditingElsewhere(item) ? editorOpenReasonId(item) : undefined"
            data-action="accept"
            @click="onAccept(item)"
          />
          <PaperHLBtn
            label="Reject"
            variant="ghost"
            :disabled="isActionDisabled(item)"
            :aria-describedby="isEditingElsewhere(item) ? editorOpenReasonId(item) : undefined"
            data-action="reject"
            @click="onReject(item)"
          />
          <PaperHLBtn
            :label="t('inbox.triage.edit.action')"
            variant="ghost"
            :disabled="isActionDisabled(item)"
            :aria-describedby="isEditingElsewhere(item) ? editorOpenReasonId(item) : undefined"
            data-action="edit"
            @click="onEdit(item)"
          />
        </div>

        <p
          v-if="isEditingElsewhere(item)"
          :id="editorOpenReasonId(item)"
          class="paper-triage__edit-block paper-triage__edit-block--row"
          role="status"
          data-testid="capture-editor-open-block"
        >
          {{ t('inbox.triage.edit.blocked.editorOpen') }}
        </p>

        <div v-if="isEditing(item)" class="paper-triage__edit">
          <p
            class="paper-triage__edit-block"
            role="status"
            data-testid="capture-edit-decision-block"
          >
            {{ t('inbox.triage.edit.decisionBlocked') }}
          </p>
          <!--
            The editor's Save writes through `captureStore.updateSuggestion`,
            which takes the SAME single busy slot Accept and Reject take. A save
            started while another row's mutation is in flight overwrites that
            row's slot, and the `actionBusyItemId` watch above then drops its
            recorded intent — the row loses its "Sending to Review…" narration
            mid-flight, and the early release re-opens a second enqueue. The
            editor cannot see the slot, so the table hands it over.
          -->
          <PaperTriageRowEdit
            :item-id="item.id"
            :mutation-in-flight="hasMutationInFlight"
            @close="closeEdit"
          />
        </div>

        <p
          v-if="decisionLine(item)"
          class="paper-triage__decision"
          :data-row-state="rowState(item)"
          data-testid="capture-row-status"
          role="status"
        >
          {{ decisionLine(item) }}
        </p>
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
.paper-triage__board-hint {
  margin: 0;
  font-family: var(--mono);
  font-size: 11px;
  color: var(--mute);
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
.paper-triage__board-reason {
  margin: 0;
  max-width: 34ch;
  font-family: var(--sans);
  font-size: 12px;
  line-height: 1.4;
  color: var(--overdue);
}

.paper-triage__edit {
  grid-column: 1 / -1;
}
.paper-triage__edit-block {
  margin: 6px 0 0;
  font-family: var(--sans);
  font-size: 12px;
  line-height: 1.4;
  color: var(--ink-2);
}
/*
 * The same note, but hung directly off the row grid rather than inside the
 * editor block — a sibling row states why its buttons are off without an
 * editor of its own to sit under.
 */
.paper-triage__edit-block--row {
  grid-column: 1 / -1;
}

.paper-triage__decision {
  grid-column: 1 / -1;
  margin: 4px 0 0;
  font-family: var(--sans);
  font-size: 12px;
  line-height: 1.4;
  color: var(--ink-2);
}
.paper-triage__decision[data-row-state='applied'] {
  color: var(--applied);
}
.paper-triage__decision[data-row-state='failed'] {
  color: var(--overdue);
}
.paper-triage__decision[data-row-state='rejected'] {
  color: var(--mute);
}

/*
 * A source tag says how the capture ARRIVED (Typed, Voice, Import); a state tag
 * says where it stands. They sat in the same visual style, so `TYPED` read as a
 * fourth state next to NEW / READY FOR REVIEW / APPLIED TO BOARD (#1944). The
 * dashed hairline marks the source tag as a different kind of fact; the `title`
 * on each tag names the kind in words.
 */
.paper-triage__tag--source {
  border-style: dashed;
}
</style>
