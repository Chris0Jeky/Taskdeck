<script setup lang="ts">
import { computed, nextTick, onMounted, ref, shallowRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import PaperHLBtn from '../../../components/paper/PaperHLBtn.vue'
import PaperTagstamp from '../../../components/paper/PaperTagstamp.vue'
import PaperTriageRowEdit, {
  type PaperTriageDraft,
  type PaperTriageDraftReport,
} from './PaperTriageRowEdit.vue'
import { useBoardStore } from '../../../store/boardStore'
import {
  canMutateSelection,
  captureRowState,
  sourceLabel,
  statusLabel,
  triageDegradedNotice,
  triageDegradedReviewKey,
} from '../../../components/inbox/inboxUtils'
import type { CaptureRowState } from '../../../components/inbox/inboxUtils'
import type { Board } from '../../../types/board'
import type { CaptureItem, CaptureItemSummary, CaptureStatusValue } from '../../../types/capture'

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
 *
 * `readOnly` is archived-board capture history (#1973). It strips every write
 * affordance, which would otherwise leave the row with nothing but a truncated
 * `textExcerpt` and a dead open button — the retained capture would be
 * "reachable" in name only. So read-only mode trades the triage controls for an
 * INSPECTION surface: the open button expands the row into the full retained
 * text and its triage provenance, supplied by the parent through `detail*`.
 * Loading and errors are the parent's too; this table only renders them.
 */
const props = withDefaults(defineProps<{
  items: CaptureItemSummary[]
  loadingList?: boolean
  listError?: string | null
  /** True while the retained rows belong to a route scope being replaced. */
  scopeReplacement?: boolean
  actionBusyItemId?: string | null
  triagePollingItemId?: string | null
  scopeLabel?: string
  scopeClearLabel?: string
  readOnly?: boolean
  /** Row whose read-only detail is expanded; `null` collapses every row. */
  detailItemId?: string | null
  detail?: CaptureItem | null
  detailLoading?: boolean
  detailError?: string | null
  /**
   * Path to the expanded capture's decision record, when one was recorded.
   * A plain path string, matching `proposalHref` on the Review side.
   */
  detailProposalRoute?: string | null
}>(), {
  readOnly: false,
  scopeReplacement: false,
  detailItemId: null,
  detail: null,
  detailLoading: false,
  detailError: null,
  detailProposalRoute: null,
})

const emit = defineEmits<{
  (event: 'accept', itemId: string, boardId?: string | null): void
  (event: 'keep', itemId: string): void
  (event: 'reject', itemId: string): void
  (event: 'open', itemId: string): void
  (event: 'retry'): void
  (event: 'clear-scope'): void
}>()

const boardStore = useBoardStore()
const { t } = useI18n()

// Item currently awaiting a board choice before its triage can be accepted.
const boardPickItemId = ref<string | null>(null)
const pickedBoardId = ref<string | null>(null)
type BoardListLoadState = 'idle' | 'loading' | 'loaded' | 'failed'
const boardListLoadState = ref<BoardListLoadState>(
  boardStore.boards.length > 0 ? 'loaded' : 'idle',
)

// Row whose text is open for pre-triage correction (GH-1951). One at a time:
// the editor holds an unsaved draft, and a second open row would give the user
// two drafts and one Save.
const editItemId = ref<string | null>(null)

/**
 * How the open editor's row is named once it is gone.
 *
 * Recorded when the editor opens, because the receipt below is written at the
 * moment the row LEAVES the list — by then the summary that carries the
 * excerpt is no longer in `items`, and a receipt that cannot say which capture
 * it is about is barely a receipt.
 */
const editItemLabel = ref<string | null>(null)

/**
 * The open editor, so the table can read its unsaved draft at the one moment
 * the close is involuntary (#1999 item 3).
 *
 * `shallowRef` and an array: the ref sits inside `v-for`, so Vue maintains it
 * as a list. Only one editor is ever mounted (`isEditing`), so the list holds
 * at most one entry.
 */
const openEditorRefs = shallowRef<InstanceType<typeof PaperTriageRowEdit>[]>([])

/**
 * Unsaved corrections whose captures are currently off the list (#1999 item 3).
 *
 * Switching the Inbox board filter replaces `items`; a row being edited is
 * usually not in the replacement, and the watcher below then closes its
 * editor. That used to destroy the draft without a word — the same silent loss
 * the sibling-edit gate and the decision block already refuse one row over.
 *
 * So the draft is held here instead, keyed by capture id, and put back when
 * the capture returns. This is COMPONENT state and nothing else: nothing is
 * persisted, and nothing reaches the server until the user presses Save in a
 * restored editor exactly as they would have before. It lives as long as the
 * table does, which is the span of the promise the receipt makes.
 */
type KeptDraft = { itemId: string; label: string; draft: PaperTriageDraft }
const keptDrafts = ref(new Map<string, KeptDraft>())

/**
 * What the notices region says, and the rule for what stands there (#1999).
 *
 * ONE LINE PER CAPTURE, and lines for different captures never displace each
 * other: a receipt saying one correction was dropped cannot be wiped by
 * another correction coming back in the same list change.
 *
 * Two kinds of line, with deliberately different lifetimes.
 *
 * STANDING lines (`held`, `blocked`, `heldUneditable`) are DERIVED from the
 * current state rather than pushed by an event. While a correction is held and
 * its capture is on this list, the line is recomputed from `items`,
 * `keptDrafts` and the open editor, so it is always true, it states itself for
 * every capture that qualifies (never just the last one, never a missed
 * second), and it disappears on its own the moment it stops being true. They
 * are not dismissable: they are the current situation, not news.
 *
 * RECEIPTS (`kept`, `restored`, `discarded`) record something that happened at
 * a moment. One per capture, replaced by that capture's next receipt, cleared
 * when the edit they describe ends from a loaded editor, and dismissable. A
 * capture with a standing line shows that instead — the current truth outranks
 * the history.
 */
type DraftLineKind = 'kept' | 'restored' | 'discarded' | 'held' | 'blocked' | 'heldUneditable'
type DraftLine = {
  captureId: string
  kind: DraftLineKind
  capture: string
  status: string
  dismissable: boolean
}
const draftReceipts = ref(new Map<string, DraftLine>())

function setReceipt(
  kind: 'kept' | 'restored' | 'discarded',
  captureId: string,
  capture: string,
  status = '',
) {
  draftReceipts.value.set(captureId, { captureId, kind, capture, status, dismissable: true })
}

/**
 * Statuses in which the SERVER itself would refuse a text edit.
 *
 * `CaptureService.IsSuggestionEditableStatus` allows New, Failed and Triaged,
 * and `Triaging` is a transient the capture is only passing through — so the
 * settled refusals are exactly these three. A held correction is dropped for
 * one of them and nothing else.
 *
 * Deliberately NOT this list's own `canMutate` gate: dropping a correction is
 * irreversible, and that gate is a product policy (#1999 item 2, the D-13
 * ruling on editing Triaged rows) which a decision can move. A Triaged capture
 * keeps its correction and says the list will not edit it; only the server's
 * settled no drops one. An out-of-contract status is not in the set either:
 * the conservative answer to "I do not recognise this" is to keep.
 */
function isPastEditing(status: CaptureStatusValue): boolean {
  return status === 3 || status === 'ProposalCreated' ||
    status === 4 || status === 'Converted' ||
    status === 5 || status === 'Ignored'
}

/**
 * The capture as this list names it. The excerpt is what the row shows, so the
 * line and the row agree; the id is the fallback the open button already uses
 * when there is nothing else to say.
 */
function captureLabel(item: CaptureItemSummary): string {
  const excerpt = typeof item.textExcerpt === 'string' ? item.textExcerpt.trim() : ''
  return excerpt || item.id
}

function restoredDraftFor(item: CaptureItemSummary): PaperTriageDraft | null {
  return keptDrafts.value.get(item.id)?.draft ?? null
}

/**
 * Why a held correction is not simply being offered back right now: another
 * editor owns the single slot, or this list will not edit the capture in the
 * state it is in (a Triaged row under the current gate, or one still being
 * triaged).
 */
function standingLineKind(item: CaptureItemSummary): DraftLineKind {
  if (editItemId.value !== null) return 'blocked'
  return canMutate(item) ? 'held' : 'heldUneditable'
}

const draftNoticeLines = computed<DraftLine[]>(() => {
  const lines: DraftLine[] = []
  const standing = new Set<string>()
  // Archived history has no Edit affordance to point at, so it carries the
  // receipts only; nothing there invites the reader to bring a correction back.
  if (!props.readOnly) {
    for (const item of props.items) {
      const kept = keptDrafts.value.get(item.id)
      // A capture whose editor is open needs no line: the correction is either
      // in the textarea or about to be, in front of the reader.
      if (!kept || editItemId.value === item.id) continue
      standing.add(item.id)
      lines.push({
        captureId: item.id,
        kind: standingLineKind(item),
        capture: kept.label,
        status: statusLabel(item.status),
        dismissable: false,
      })
    }
  }
  for (const receipt of draftReceipts.value.values()) {
    if (standing.has(receipt.captureId)) continue
    lines.push(receipt)
  }
  return lines
})

const hasDismissableNotice = computed(() => draftNoticeLines.value.some((line) => line.dismissable))

/**
 * Dismiss the receipts. Standing lines are untouched, because they describe
 * corrections that are still held — dismissing those would be the surface
 * forgetting out loud.
 *
 * Focus moves to the region itself, which stays mounted: the same rule
 * `closeLoadingDetail` follows, so removing what the reader was on does not
 * drop focus to the document.
 */
async function dismissDraftNotices(event: MouseEvent) {
  const region = (event.currentTarget as HTMLElement | null)
    ?.closest<HTMLElement>('.paper-triage__draft-notices')
  draftReceipts.value.clear()
  await nextTick()
  region?.focus()
}

function readOpenEditor(): PaperTriageDraftReport {
  return openEditorRefs.value[0]?.readDraft() ?? { state: 'unavailable' }
}

/**
 * Take the open editor's unsaved correction into the held map before the
 * editor goes away.
 *
 * The three-way answer is the point. `ready` with a draft holds it; `ready`
 * with none releases what was held, because a correction typed back to the
 * server's own text is no longer a correction; and `unavailable` — loading,
 * failed to load, or refused — changes NOTHING, because an editor that never
 * showed the reader a textarea is no evidence at all about what is held. The
 * older shape collapsed the last two into `null` and quietly destroyed a
 * correction whose restore had not finished loading.
 */
function keepOpenDraft() {
  const itemId = editItemId.value
  if (itemId === null) return
  const label = editItemLabel.value ?? itemId
  const report = readOpenEditor()
  if (report.state === 'ready') {
    if (report.draft === null) {
      keptDrafts.value.delete(itemId)
      draftReceipts.value.delete(itemId)
      return
    }
    keptDrafts.value.set(itemId, { itemId, label, draft: report.draft })
  }
  if (keptDrafts.value.has(itemId)) setReceipt('kept', itemId, label)
}

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
type PendingAction = { itemId: string; kind: 'accept' | 'keep' | 'reject' }
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

/**
 * What a list replacement does to an unsaved correction (#1999 item 3).
 *
 * The board filter is not visible from here: switching it makes the
 * orchestrator load the new scope and hand this table a different `items`
 * array, which is also what a refresh that no longer returns the row does. One
 * watcher therefore answers both.
 *
 * It does two things and no more. Nothing here reopens an editor: a held
 * correction comes back only through the reader's own Edit, so no editor
 * appears seeded with text nobody asked for, no board pick in progress is
 * cancelled by a list arriving, and a return that cannot be honoured needs no
 * silent exception. The offer to bring it back is a standing line, derived
 * from the current state rather than pushed from here, so it cannot miss a
 * second correction or go stale.
 */
watch(
  () => props.items,
  (rows) => {
    // A held correction is dropped only when its capture has settled where the
    // server itself would refuse the edit. A capture whose editor is OPEN is
    // never swept: the correction is on screen, so announcing it was dropped
    // would be false, and dropping it would take what the reader is looking at.
    for (const row of rows) {
      const kept = keptDrafts.value.get(row.id)
      if (!kept || editItemId.value === row.id || !isPastEditing(row.status)) continue
      keptDrafts.value.delete(row.id)
      setReceipt('discarded', row.id, kept.label, statusLabel(row.status))
    }

    // The edited row is not in the replacement. The editor still closes — the
    // row it belongs to is gone — but its draft is taken first.
    if (editItemId.value !== null && !rows.some((row) => row.id === editItemId.value)) {
      keepOpenDraft()
      editItemId.value = null
      editItemLabel.value = null
    }
  },
)

watch(
  () => props.readOnly,
  (readOnly) => {
    if (!readOnly) return
    // Entering archived history closes the editor through this watcher rather
    // than through the list, so it is a second door onto the same loss: take
    // the correction the same way, and say so.
    keepOpenDraft()
    editItemId.value = null
    editItemLabel.value = null
    boardPickItemId.value = null
    pickedBoardId.value = null
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
 * Empty-list truth comes first: a request in flight or a failed request is not
 * evidence that the account has no boards. Once a successful load establishes
 * the list, "nothing picked" is reported before "not writable", because
 * `pickedBoardIsWritable` is also false when nothing is picked.
 */
type BoardPickBlock = 'loading' | 'loadFailed' | 'noBoards' | 'noBoard' | 'viewOnly'

const boardPickBlock = computed<BoardPickBlock | null>(() => {
  if (boardStore.boards.length === 0) {
    if (boardListLoadState.value === 'failed') return 'loadFailed'
    if (boardListLoadState.value !== 'loaded') return 'loading'
    return 'noBoards'
  }
  if (!pickedBoardId.value) return 'noBoard'
  if (!pickedBoardIsWritable.value) return 'viewOnly'
  return null
})

const boardPickBlockMessage = computed(() => {
  if (boardPickBlock.value === 'loading' || boardPickBlock.value === 'loadFailed') {
    return t(`inbox.triage.boardPick.${boardPickBlock.value}`)
  }
  return boardPickBlock.value ? t(`inbox.triage.boardPick.blocked.${boardPickBlock.value}`) : ''
})

function boardPickReasonId(item: CaptureItemSummary): string {
  return `board-pick-reason-${item.id}`
}

const hasItems = computed(() => props.items.length > 0)

/**
 * A list load running over rows that stay on screen (#2501).
 *
 * A scope replacement is excluded: those rows belong to the scope being left,
 * so they are hidden and the "Loading…" empty state speaks for them instead.
 * A failed load is excluded too — the error takes the header's place.
 */
const isBackgroundRefresh = computed(
  () => props.loadingList && !props.scopeReplacement && !props.listError && hasItems.value,
)
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
  return props.readOnly ||
    hasMutationInFlight.value ||
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
  // Recorded here because the line about this correction is written at the
  // moment the row LEAVES the list, when the summary carrying the excerpt is
  // no longer in `items`.
  editItemLabel.value = captureLabel(item)
}

/**
 * The editor emitted `close` — from FOUR places with two different meanings,
 * so the editor is asked which before anything is released.
 *
 * Cancel and a landed Save come from a loaded editor: the correction is either
 * on the server or deliberately abandoned, and the receipts about it are spent
 * with it. Cancel on the load-error panel and Close on the refused panel come
 * from an editor that never showed a textarea — those buttons answer the
 * failed read, not a correction the reader was never shown, so what is held
 * stays held and says so.
 */
function closeEdit() {
  const itemId = editItemId.value
  if (itemId !== null) {
    const report = readOpenEditor()
    if (report.state === 'ready') {
      keptDrafts.value.delete(itemId)
      draftReceipts.value.delete(itemId)
    } else if (keptDrafts.value.has(itemId)) {
      setReceipt('kept', itemId, editItemLabel.value ?? itemId)
    }
  }
  editItemId.value = null
  editItemLabel.value = null
}

/** The editor has put a held correction back; say which one, once it is true. */
function onDraftRestored(itemId: string) {
  const label = keptDrafts.value.get(itemId)?.label ?? editItemLabel.value ?? itemId
  setReceipt('restored', itemId, label)
}

function hasBoard(item: CaptureItemSummary): boolean {
  return typeof item.boardId === 'string' && item.boardId.length > 0
}

function isPickingBoard(item: CaptureItemSummary): boolean {
  return boardPickItemId.value === item.id
}

async function loadBoardsForPicker() {
  if (boardListLoadState.value === 'loading') return
  if (boardStore.boards.length > 0) {
    boardListLoadState.value = 'loaded'
    return
  }

  boardListLoadState.value = 'loading'
  try {
    await boardStore.fetchBoards()
    boardListLoadState.value = 'loaded'
  } catch {
    boardListLoadState.value = 'failed'
  }
}

function retryBoardLoad() {
  void loadBoardsForPicker()
}

function onAccept(item: CaptureItemSummary) {
  if (isActionDisabled(item)) return
  if (hasBoard(item)) {
    pendingAction.value = { itemId: item.id, kind: 'accept' }
    emit('accept', item.id, item.boardId)
    return
  }
  // No board yet — require the user to choose one before we queue triage.
  pickedBoardId.value = null
  boardPickItemId.value = item.id
  if (boardStore.boards.length === 0 && boardListLoadState.value === 'idle') {
    void loadBoardsForPicker()
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

function onKeep(item: CaptureItemSummary) {
  if (isActionDisabled(item)) return
  pendingAction.value = { itemId: item.id, kind: 'keep' }
  emit('keep', item.id)
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
type TriageRowState = CaptureRowState | 'keeping' | 'archiving' | 'kept' | 'archived'

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
    if (pending.kind === 'keep') return 'keeping'
    if (pending.kind === 'reject') return 'archiving'
    return 'sending'
  }
  if (item.disposition?.kind === 'Kept' || item.disposition?.kind === 0) return 'kept'
  if (item.disposition?.kind === 'Archived' || item.disposition?.kind === 1) return 'archived'
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

/**
 * The degradation notice for a row whose triage SUCCEEDED on the deterministic
 * extractor after its LLM leg could not deliver (#2202).
 *
 * Deliberately a second, separate accessor rather than a widening of
 * `failureReason`: the two are mutually exclusive by construction (that one
 * fires only on the `overdue` tone, this one only on a completed status), and
 * they must stay so. `failureReason` renders inside the row button with
 * `role="alert"` in the failure tone; this renders as its own `role="status"`
 * block outside the button. Merging them is what would produce "Triage failed"
 * over a capture that did not fail.
 *
 * Paper has no capture detail panel outside read-only history, so the row is
 * the ONLY place a Paper user can be told — which is why it carries the whole
 * notice (who produced it, what the server reported, what it means for review,
 * what to check) rather than a one-line teaser.
 */
function degradedNotice(item: CaptureItemSummary): string | null {
  return triageDegradedNotice(item)
}

/**
 * Which `inbox.degraded.review*` sentence is true for this row's status (PR
 * #2224 review): a `Triaged` row has no proposal to apply and a `Converted`
 * row was applied already, so the apply guidance is only right in between.
 */
function degradedReviewKey(item: CaptureItemSummary): string {
  return triageDegradedReviewKey(item)
}

function degradedNoticeId(item: CaptureItemSummary): string {
  return `paper-capture-degraded-${item.id}`
}

onMounted(() => {
  // Prime boards so the picker is ready if the user accepts a board-less capture.
  // Archived history has no Accept path, so it must not prime the picker.
  if (!props.readOnly && boardStore.boards.length === 0) {
    void loadBoardsForPicker()
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

// ---- Read-only capture inspection (#1973) ----

/** Whether this row's retained-capture detail is currently expanded. */
function isDetailOpen(item: CaptureItemSummary): boolean {
  return props.readOnly && props.detailItemId === item.id
}

/**
 * Keep the archived disclosure name useful without turning it into a copy of
 * the retained capture. The excerpt is already the bounded row summary; the
 * timestamp and id are deterministic non-secret fallbacks for empty or invalid
 * summaries.
 */
function detailCaptureLabel(item: CaptureItemSummary): string {
  const excerpt = item.textExcerpt.trim().replace(/\s+/g, ' ')
  if (excerpt) return excerpt.length > 80 ? `${excerpt.slice(0, 77)}…` : excerpt
  return formatDateTime(item.createdAt) || item.id
}

function detailToggleLabel(item: CaptureItemSummary): string {
  return t(
    isDetailOpen(item) ? 'inbox.history.detail.closeFor' : 'inbox.history.detail.openFor',
    { capture: detailCaptureLabel(item) },
  )
}

function detailPanelId(item: CaptureItemSummary): string {
  return `paper-capture-detail-${item.id}`
}

/**
 * Collapse a stalled read-only load without cancelling its request. The parent
 * already rejects a late payload by item id; this component restores focus to
 * the disclosure that remains after the loading panel is removed.
 */
async function closeLoadingDetail(itemId: string, event: MouseEvent) {
  const opener = (event.currentTarget as HTMLElement | null)
    ?.closest('.paper-triage__row')
    ?.querySelector<HTMLButtonElement>('.paper-triage__open')

  emit('open', itemId)
  await nextTick()
  opener?.focus()
}

/**
 * The loaded detail, but only while it still belongs to the expanded row.
 * The parent loads asynchronously, so between "row B opened" and "row B's
 * detail arrived" the stale row-A payload must not render under row B.
 */
const activeDetail = computed<CaptureItem | null>(() => {
  const detail = props.detail
  if (!detail || !props.detailItemId) return null
  return detail.id === props.detailItemId ? detail : null
})

function formatDateTime(iso: string | null | undefined): string {
  if (!iso) return ''
  try {
    const d = new Date(iso)
    if (Number.isNaN(d.getTime())) return ''
    return d.toLocaleString()
  } catch {
    return ''
  }
}

/** A recorded value, or the explicit "not recorded" placeholder — never blank. */
function recordedOr(value: string | null | undefined): string {
  const trimmed = typeof value === 'string' ? value.trim() : ''
  return trimmed || t('inbox.history.detail.none')
}
</script>

<template>
  <section
    class="paper-triage"
    :aria-label="t('inbox.triage.tableAria')"
    :aria-busy="loadingList && !listError"
  >
    <header class="paper-triage__header">
      <h2 class="tk-h3 paper-triage__title">{{ readOnly ? t('inbox.history.tableTitle') : "Today's captures" }}</h2>
      <span v-if="!scopeReplacement && (hasItems || (!loadingList && !listError))" class="tk-meta">
        {{ hasItems ? `${items.length} item${items.length === 1 ? '' : 's'} · most recent first` : 'No captures yet' }}
        <!--
          A same-scope refresh keeps these rows mounted, visible and usable
          (#2501). `aria-busy` on the section already says a load is running,
          but nothing said so to a sighted user. Row actions stay enabled on
          purpose: the rows are still the right rows for this scope.
        -->
        <span
          v-if="isBackgroundRefresh"
          class="paper-triage__refreshing"
          data-testid="paper-triage-refreshing"
        >&nbsp;· {{ t('inbox.refreshing') }}</span>
      </span>
    </header>

    <!--
      Where an unsaved correction stands once its capture left the list (#1999
      item 3). It sits above the list rather than on a row because the capture
      it is about is often not on screen, and because it outlives the row that
      produced it.

      MOUNTED AND SILENT until it has something to say (#2593/#2630): a live
      region inserted at the same moment its text appears is announced
      unreliably, so the region is always here and only its lines come and go.
      It is never `display: none` either, which would take it back out of the
      accessibility tree and undo exactly that.

      `role="status"` and not `alert`: five of the six lines are about a
      correction that is safe, and the sixth is a stated consequence rather
      than a failure. `aria-atomic="false"` because the region carries one
      independent line per capture — a change about one correction must not
      re-read the others.
    -->
    <div
      class="paper-triage__draft-notices"
      :class="{ 'paper-triage__draft-notices--speaking': draftNoticeLines.length > 0 }"
      role="status"
      aria-live="polite"
      aria-atomic="false"
      tabindex="-1"
      data-testid="capture-draft-notices"
    >
      <p
        v-for="line in draftNoticeLines"
        :key="line.captureId"
        class="paper-triage__draft-notice"
        :data-notice="line.kind"
        :data-capture-id="line.captureId"
      >
        {{ t(`inbox.triage.draft.${line.kind}`, { capture: line.capture, status: line.status }) }}
      </p>
      <button
        v-if="hasDismissableNotice"
        type="button"
        class="paper-triage__retry paper-triage__draft-dismiss"
        data-action="dismiss-draft-notice"
        @click="dismissDraftNotices"
      >
        {{ t('inbox.triage.draft.dismiss') }}
      </button>
    </div>

    <div v-if="listError" class="paper-triage__empty paper-triage__empty--error" role="alert">
      <p class="tk-body">{{ listError }}</p>
      <button type="button" class="paper-triage__retry" @click="emit('retry')">
        Retry
      </button>
    </div>

    <div v-else-if="loadingList && (scopeReplacement || !hasItems)" class="paper-triage__empty" role="status">
      <span class="tk-meta">Loading…</span>
    </div>

    <div v-else-if="!hasItems" class="paper-triage__empty">
      <template v-if="scopeLabel">
        <p class="tk-body">{{ t('inbox.empty.scoped', { scope: scopeLabel }) }}</p>
        <button type="button" class="paper-triage__retry" data-testid="paper-triage-clear-scope" @click="emit('clear-scope')">
          {{ scopeClearLabel }}
        </button>
      </template>
      <p v-else class="tk-body">
        {{ readOnly ? t('inbox.history.empty') : 'A pen and a phrase. Drop a thought above to start.' }}
      </p>
    </div>

    <!--
      Keep retained rows mounted while a replacement load hides them. The row
      editor owns its unsaved draft locally, so unmounting this list during a
      same-scope refresh would silently replace that draft with server text on
      remount. Conditional display preserves the subtree without exposing
      stale rows from a route-scope replacement.
    -->
    <ul
      v-if="hasItems"
      class="paper-triage__list"
      :style="scopeReplacement ? { display: 'none' } : undefined"
    >
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
          :aria-label="readOnly ? detailToggleLabel(item) : `Open capture ${item.id}`"
          :aria-expanded="readOnly ? isDetailOpen(item) : undefined"
          :aria-controls="readOnly ? detailPanelId(item) : undefined"
          :aria-describedby="degradedNotice(item) ? degradedNoticeId(item) : undefined"
          :data-testid="readOnly ? 'capture-history-open' : undefined"
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

        <!--
          Degraded triage (#2202). A capture whose LLM leg could not deliver
          still TRIAGED, so this is a caution on a success and is built to be
          unmistakable for the failure above it: its own block outside the row
          button (not a span inside the button's accessible name), `role="status"`
          rather than `alert`, and the note palette rather than `--overdue`.
          The server's notice renders verbatim — it is already redacted and
          bounded upstream, and nothing local is ever appended to it.
        -->
        <div
          v-if="degradedNotice(item)"
          :id="degradedNoticeId(item)"
          class="paper-triage__degraded"
          role="status"
          data-testid="capture-degraded-notice"
        >
          <p class="paper-triage__degraded-label">{{ t('inbox.degraded.label') }}</p>
          <p class="paper-triage__degraded-line">{{ t('inbox.degraded.lead') }}</p>
          <p class="paper-triage__degraded-reason" data-testid="capture-degraded-reason">
            {{ t('inbox.degraded.reason', { reason: degradedNotice(item) }) }}
          </p>
          <p class="paper-triage__degraded-line">{{ t(degradedReviewKey(item)) }}</p>
          <p class="paper-triage__degraded-line">{{ t('inbox.degraded.action') }}</p>
        </div>

        <!--
          Read-only capture inspection (#1973). Archived history has no triage
          controls, so this expanded panel is the ONLY way to see the retained
          capture in full — the row above shows a truncated excerpt. It renders
          text and provenance and links to the decision record; it has no
          control that writes.
        -->
        <div
          v-if="isDetailOpen(item)"
          :id="detailPanelId(item)"
          class="paper-triage__history-detail"
          data-testid="capture-history-detail"
        >
          <template v-if="detailLoading && !activeDetail">
            <p class="tk-body" role="status">
              {{ t('inbox.history.detail.loading') }}
            </p>
            <PaperHLBtn
              class="paper-triage__history-close"
              :label="t('inbox.history.detail.close')"
              variant="ghost"
              data-testid="capture-history-loading-close"
              @click="closeLoadingDetail(item.id, $event)"
            />
          </template>
          <p
            v-else-if="detailError"
            class="tk-body paper-triage__history-error"
            role="alert"
            data-testid="capture-history-detail-error"
          >
            {{ detailError }}
          </p>
          <template v-else-if="activeDetail">
            <h3 class="tk-eyebrow">{{ t('inbox.history.detail.title') }}</h3>
            <p class="tk-body paper-triage__history-text" data-testid="capture-history-text">{{ activeDetail.rawText }}</p>
            <dl class="paper-triage__history-meta">
              <div>
                <dt class="tk-eyebrow">{{ t('inbox.history.detail.captured') }}</dt>
                <dd class="tk-meta">{{ recordedOr(formatDateTime(activeDetail.createdAt)) }}</dd>
              </div>
              <div>
                <dt class="tk-eyebrow">{{ t('inbox.history.detail.processed') }}</dt>
                <dd class="tk-meta">{{ recordedOr(formatDateTime(activeDetail.processedAt)) }}</dd>
              </div>
              <div>
                <dt class="tk-eyebrow">{{ t('inbox.history.detail.board') }}</dt>
                <dd class="tk-meta">{{ recordedOr(activeDetail.boardId) }}</dd>
              </div>
              <div>
                <dt class="tk-eyebrow">{{ t('inbox.history.detail.triageRun') }}</dt>
                <dd class="tk-meta">{{ recordedOr(activeDetail.provenance?.triageRunId) }}</dd>
              </div>
              <div>
                <dt class="tk-eyebrow">{{ t('inbox.history.detail.promptVersion') }}</dt>
                <dd class="tk-meta">{{ recordedOr(activeDetail.provenance?.promptVersion) }}</dd>
              </div>
            </dl>
            <RouterLink
              v-if="detailProposalRoute"
              class="paper-triage__history-link"
              :to="detailProposalRoute"
              data-testid="capture-history-proposal-link"
            >
              {{ t('inbox.history.detail.proposalLink') }}
            </RouterLink>
            <p v-else class="tk-meta" data-testid="capture-history-no-proposal">
              {{ t('inbox.history.detail.noProposal') }}
            </p>
          </template>
        </div>

        <div
          v-if="!readOnly && isPickingBoard(item)"
          class="paper-triage__board-pick"
          data-testid="capture-board-pick"
          :aria-busy="boardPickBlock === 'loading'"
        >
          <label class="paper-triage__board-label">
            <span class="tk-eyebrow">{{ t('inbox.boardPicker.label') }}</span>
            <select
              v-model="pickedBoardId"
              class="paper-triage__board-select"
              :aria-label="t('inbox.boardPicker.triageAria')"
              :aria-describedby="boardPickBlock ? boardPickReasonId(item) : undefined"
              :disabled="boardStore.boards.length === 0"
            >
              <option :value="null" disabled>{{ t('inbox.boardPicker.selectPlaceholder') }}</option>
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
            :role="boardPickBlock === 'loadFailed' ? 'alert' : 'status'"
            data-testid="board-pick-reason"
            :data-reason="boardPickBlock"
          >
            {{ boardPickBlockMessage }}
          </p>
          <button
            v-if="boardPickBlock === 'loadFailed'"
            type="button"
            class="paper-triage__retry"
            data-action="retry-board-load"
            :aria-describedby="boardPickReasonId(item)"
            @click="retryBoardLoad"
          >
            {{ t('inbox.triage.boardPick.retry') }}
          </button>
          <div class="paper-triage__actions">
            <PaperHLBtn
              label="Ask AI for proposal"
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

        <div v-else-if="!readOnly" class="paper-triage__actions">
          <PaperHLBtn
            label="Ask AI"
            variant="ember"
            :disabled="isActionDisabled(item)"
            :aria-describedby="isEditingElsewhere(item) ? editorOpenReasonId(item) : undefined"
            data-action="accept"
            @click="onAccept(item)"
          />
          <PaperHLBtn
            label="Keep"
            variant="ghost"
            :disabled="isActionDisabled(item)"
            :aria-describedby="isEditingElsewhere(item) ? editorOpenReasonId(item) : undefined"
            data-action="keep"
            @click="onKeep(item)"
          />
          <PaperHLBtn
            label="Archive"
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
          v-if="!readOnly && isEditingElsewhere(item)"
          :id="editorOpenReasonId(item)"
          class="paper-triage__edit-block paper-triage__edit-block--row"
          role="status"
          data-testid="capture-editor-open-block"
        >
          {{ t('inbox.triage.edit.blocked.editorOpen') }}
        </p>

        <div v-if="!readOnly && isEditing(item)" class="paper-triage__edit">
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
          <!--
            `restored-draft` is the correction this table held while the
            capture was off the list (#1999). It carries only the fields the
            reader actually changed, and the editor lays those over the values
            it re-reads on mount — so an untouched due date or label is the
            server's current one, not the one the old load happened to see.
            Save is still last-write-wins, exactly as it is from an editor
            opened fresh: the hold buys no extra staleness, and claims none.
          -->
          <PaperTriageRowEdit
            ref="openEditorRefs"
            :item-id="item.id"
            :mutation-in-flight="hasMutationInFlight"
            :restored-draft="restoredDraftFor(item)"
            @close="closeEdit"
            @restored="onDraftRestored"
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
/* The same-scope refresh note, set apart from the count it follows (#2501). */
.paper-triage__refreshing {
  font-style: italic;
  opacity: 0.75;
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
/*
 * The draft-fate region (#1999 item 3). Set in the note palette rather than
 * the failure one: five of its six sentences are about a correction that is
 * safe, and the sixth is a stated consequence, not an error.
 *
 * The region is always mounted, so the bare class must occupy nothing — no
 * border, no padding, no margin. `display: none` is deliberately NOT used: it
 * would remove the live region from the accessibility tree, which is the one
 * thing being always-mounted exists to avoid.
 */
.paper-triage__draft-notices:focus {
  outline: none;
}
.paper-triage__draft-notices--speaking {
  margin-bottom: 12px;
  padding: 10px 12px;
  border: 1px solid var(--line-soft);
  border-left: 2px solid var(--ember);
  border-radius: 3px;
  background: var(--paper-card);
}
.paper-triage__draft-notice {
  margin: 0 0 4px;
  max-width: 72ch;
  font-family: var(--sans);
  font-size: 12px;
  line-height: 1.4;
  color: var(--ink-2);
  overflow-wrap: anywhere;
}
.paper-triage__draft-dismiss {
  margin-top: 6px;
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
  flex-wrap: wrap;
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

/*
 * Degraded-triage note (#2202). Deliberately NOT `--overdue`: that colour is
 * the failure tone this row did not earn. A quiet panel with an ember rule
 * reads as an annotation on a result, which is what it is.
 */
.paper-triage__degraded {
  grid-column: 1 / -1;
  margin: 6px 0 0;
  padding: 8px 10px;
  background: var(--paper-2);
  border-left: 2px solid var(--ember);
  border-radius: 2px;
}
.paper-triage__degraded-label {
  margin: 0 0 4px;
  font-family: var(--sans);
  font-size: 10px;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: var(--ember);
}
.paper-triage__degraded-line {
  margin: 0 0 4px;
  font-family: var(--sans);
  font-size: 12px;
  line-height: 1.4;
  color: var(--ink-2);
}
.paper-triage__degraded-line:last-child {
  margin-bottom: 0;
}
/* The server's own words, set apart from Taskdeck's explanation around them. */
.paper-triage__degraded-reason {
  margin: 0 0 4px;
  font-family: var(--mono);
  font-size: 11px;
  line-height: 1.4;
  color: var(--mute);
  overflow-wrap: anywhere;
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

/*
 * Read-only capture inspection (#1973). Sits where the triage actions would be,
 * so the archived row still has a substantive lower half. `grid-column: 1 / -1`
 * lets the retained text run the full row width instead of the excerpt column.
 */
.paper-triage__history-detail {
  grid-column: 1 / -1;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  margin-top: 0.5rem;
  padding: 0.75rem;
  border: 1px solid var(--rule);
  border-radius: 2px;
  background: var(--paper-2, transparent);
}
.paper-triage__history-text {
  /* The retained capture is the point of the panel — keep its own line breaks
     and let long single-token pastes wrap instead of forcing a scrollbar. */
  white-space: pre-wrap;
  overflow-wrap: anywhere;
  margin: 0;
}
.paper-triage__history-error {
  color: var(--overdue);
}
.paper-triage__history-close {
  align-self: flex-start;
}
.paper-triage__history-meta {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(11rem, 1fr));
  gap: 0.5rem 1rem;
  margin: 0;
}
.paper-triage__history-meta dd {
  margin: 0;
  overflow-wrap: anywhere;
}
.paper-triage__history-link {
  align-self: flex-start;
  color: var(--ink-1);
  text-decoration: underline;
}

@media (max-width: 640px) {
  .paper-triage__row {
    grid-template-columns: minmax(0, 1fr);
  }

  .paper-triage__tags,
  .paper-triage__actions,
  .paper-triage__reason {
    grid-column: 1;
  }

  .paper-triage__actions {
    justify-content: flex-start;
  }
}
</style>
