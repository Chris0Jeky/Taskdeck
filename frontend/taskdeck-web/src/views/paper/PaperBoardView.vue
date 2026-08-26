<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { useBoardStore } from '../../store/boardStore'
import { useBoardDragDrop } from '../../composables/useBoardDragDrop'
import { useViewportMode } from '../../composables/useViewportMode'
import PaperBoardColumn from './PaperBoardColumn.vue'
import PaperBoardSettingsDialog from './board/PaperBoardSettingsDialog.vue'
import PaperColumnSettingsDialog from './board/PaperColumnSettingsDialog.vue'
import PaperHLBtn from '../../components/paper/PaperHLBtn.vue'
import CardModal from '../../components/board/CardModal.vue'
import TdDialog from '../../components/ui/TdDialog.vue'
import type { Card, Column } from '../../types/board'
import type { PaperBoardCardVariant } from './PaperBoardCard.vue'
import { logError } from '../../utils/errorReporting'

/**
 * PaperBoardView — Paper / Graphite kanban surface.
 *
 * Orchestrator only. Card and column visuals live in `PaperBoardColumn.vue`
 * and `PaperBoardCard.vue`. Drag/drop reuses `useBoardDragDrop` (column
 * reorder) and `boardStore.moveCard` (card move) so the existing
 * persistence + audit-log semantics are unchanged — only the visuals differ.
 *
 * Mounted at the same route as `BoardView`; the wrapping `BoardView` shell
 * delegates to this view when `paperThemeStore.isOn`.
 *
 * Direct board management (#1945 / ADR-0056): add-card, column rename/delete,
 * column reorder and board settings all live here, driving the same
 * `boardStore` actions the Legacy skin uses. These are DIRECT human edits —
 * they take effect immediately and never open a proposal. The `+ capture`
 * affordance is the separate, secondary door into the review lane.
 */
const props = withDefaults(
  defineProps<{
    /** Card visual variant — propagated to every column. */
    cardVariant?: PaperBoardCardVariant
    selectedCardId?: string | null
  }>(),
  { cardVariant: 'index', selectedCardId: null },
)

const emit = defineEmits<{
  /**
   * Fired whenever this view's modal-dialog state changes.
   *
   * `BoardView` owns the board keyboard shortcuts but this view owns the
   * dialogs, so the gate has to be reported upward (GH-1959). Without it `n`
   * fired straight through an open dialog: it clicked
   * `[data-action="toggle-add-card"]` on the column behind the modal and then
   * yanked focus to the composer a moment later.
   */
  (event: 'dialog-open-change', open: boolean): void
}>()

const route = useRoute()
const router = useRouter()
const boardStore = useBoardStore()
const { t } = useI18n()
const { mode: viewportMode } = useViewportMode()

const boardId = computed(() => (typeof route.params.id === 'string' ? route.params.id : ''))
const selectedCard = ref<Card | null>(null)
const pendingCard = ref<Card | null>(null)
const cardEditorDirty = ref(false)
type BoardDensity = 'comfortable' | 'compact'
const BOARD_DENSITY_KEY = 'td.paper.board-density.v1'
const density = ref<BoardDensity>('comfortable')
const cardPresentation = computed(() => viewportMode.value === 'desktop' ? 'inspector' : 'modal')

onMounted(() => {
  try {
    density.value = window.localStorage.getItem(BOARD_DENSITY_KEY) === 'compact'
      ? 'compact'
      : 'comfortable'
  } catch {
    density.value = 'comfortable'
  }
})

function toggleDensity() {
  density.value = density.value === 'compact' ? 'comfortable' : 'compact'
  try {
    window.localStorage.setItem(BOARD_DENSITY_KEY, density.value)
  } catch {
    // Local fallback only. The preference remains active for this mounted board.
  }
}

const sortedColumns = computed<Column[]>(() => {
  if (!boardStore.currentBoard) return []
  return [...boardStore.currentBoard.columns].sort((a, b) => a.position - b.position)
})

const cardsByColumn = computed<Map<string, Card[]>>(() => {
  const map = new Map<string, Card[]>()

  for (const card of boardStore.currentBoardCards) {
    if (!map.has(card.columnId)) {
      map.set(card.columnId, [])
    }
    map.get(card.columnId)!.push(card)
  }

  map.forEach((cards) => {
    cards.sort((a, b) => a.position - b.position)
  })

  return map
})

const activeSelectedCardId = computed(() => props.selectedCardId ?? selectedCard.value?.id ?? null)

watch(boardId, () => {
  selectedCard.value = null
  pendingCard.value = null
  cardEditorDirty.value = false
  // Switching boards must not carry a half-typed card draft, an open column
  // dialog, or an error banner across to a board they do not belong to.
  resetBoardManagementState()
})

const totalCards = computed(() =>
  sortedColumns.value.reduce((sum, c) => sum + (cardsByColumn.value.get(c.id)?.length ?? 0), 0),
)

const {
  draggedColumn,
  dragOverColumnId,
  draggedCard,
  handleColumnDragStart,
  handleColumnDragEnd,
  handleColumnDragOver,
  handleColumnDragLeave,
  handleColumnDrop,
  handleCardDragStart,
  handleCardDragEnd,
} = useBoardDragDrop(() => boardId.value, sortedColumns)

/**
 * Drop a card onto a column — reuses `boardStore.moveCard` so the same
 * audit-log + persistence path runs as the Obsidian view.
 */
async function onCardDropOnColumn(column: Column, event: DragEvent) {
  event.preventDefault()
  if (!draggedCard.value) return
  if (draggedCard.value.columnId === column.id) {
    handleCardDragEnd()
    return
  }
  try {
    const targetCards = cardsByColumn.value.get(column.id) ?? []
    await boardStore.moveCard(boardId.value, draggedCard.value.id, column.id, targetCards.length)
  } catch (error) {
    logError('Failed to move card (paper):', error)
  } finally {
    handleCardDragEnd()
  }
}

async function onCardDropOnCard(targetCard: Card, column: Column, event: DragEvent) {
  event.preventDefault()
  event.stopPropagation()
  if (!draggedCard.value) return
  if (draggedCard.value.id === targetCard.id) {
    handleCardDragEnd()
    return
  }

  try {
    let targetPosition = targetCard.position
    if (draggedCard.value.columnId === column.id && draggedCard.value.position < targetCard.position) {
      targetPosition -= 1
    }

    await boardStore.moveCard(boardId.value, draggedCard.value.id, column.id, targetPosition)
  } catch (error) {
    logError('Failed to reorder card (paper):', error)
  } finally {
    handleCardDragEnd()
  }
}

function onCardDragOverCard(event: DragEvent) {
  if (!draggedCard.value) return
  event.preventDefault()
  event.stopPropagation()
  if (event.dataTransfer) event.dataTransfer.dropEffect = 'move'
}

function onCardDragOverColumn(column: Column, event: DragEvent) {
  // Permit cards to be dropped onto columns even if column-reorder logic
  // (which only fires when a column is being dragged) doesn't claim the
  // event. handleColumnDragOver already calls preventDefault when a column
  // is being dragged.
  if (draggedCard.value) {
    event.preventDefault()
    dragOverColumnId.value = column.id
    if (event.dataTransfer) event.dataTransfer.dropEffect = 'move'
  }
}

function isColumnDragHandleTarget(target: EventTarget | null): boolean {
  return target instanceof Element && target.closest('[data-action="drag-column-handle"]') !== null
}

function onLaneDragStart(column: Column, event: DragEvent) {
  if (isColumnDragHandleTarget(event.target)) {
    handleColumnDragStart(column, event)
  }
}

function openCard(card: Card) {
  if (selectedCard.value?.id === card.id) return
  if (selectedCard.value && cardEditorDirty.value) {
    pendingCard.value = card
    return
  }
  selectedCard.value = card
}

function closeCard() {
  selectedCard.value = null
  pendingCard.value = null
  cardEditorDirty.value = false
}

function handleCardEditorDirtyChange(dirty: boolean) {
  cardEditorDirty.value = dirty
}

function cancelCardSwitch() {
  pendingCard.value = null
}

function discardAndSwitchCard() {
  if (!pendingCard.value) return
  selectedCard.value = pendingCard.value
  pendingCard.value = null
  cardEditorDirty.value = false
}

function openCapture(_column: Column) {
  void router.push({
    name: 'workspace-inbox',
    query: { boardId: boardId.value, columnId: _column.id },
  })
}

function openReview() {
  void router.push({
    name: 'workspace-review',
    query: { boardId: boardId.value },
  })
}

function openCaptureBoard() {
  void router.push({
    name: 'workspace-inbox',
    query: { boardId: boardId.value },
  })
}

/**
 * Direct board management (#1945).
 *
 * Every handler below writes through the SAME `boardStore` actions the Legacy
 * skin uses — `createCard`, `reorderColumns`, plus `updateColumn`/
 * `deleteColumn`/`updateBoard`/`deleteBoard` inside the two dialogs. Nothing
 * here creates a proposal: a human's click on their own board is a direct
 * write, and the review lane governs agent-originated changes (ADR-0056).
 */
const composerColumnId = ref<string | null>(null)
const composerBusy = ref(false)
const composerError = ref<string | null>(null)
const editingColumn = ref<Column | null>(null)
const showBoardSettings = ref(false)

function resetBoardManagementState() {
  composerColumnId.value = null
  composerBusy.value = false
  composerError.value = null
  editingColumn.value = null
  showBoardSettings.value = false
  cancelAddColumn()
}

/**
 * The column whose settings dialog is open, re-resolved from the store on every
 * read. Holding the prop object captured at open time would render stale values
 * after a realtime refresh replaced the column, and would keep a deleted column
 * alive in the dialog.
 */
const editingColumnLive = computed<Column | null>(() => {
  const id = editingColumn.value?.id
  if (!id) return null
  return sortedColumns.value.find((column) => column.id === id) ?? null
})

const editingColumnCardCount = computed(() =>
  editingColumnLive.value ? (cardsByColumn.value.get(editingColumnLive.value.id)?.length ?? 0) : 0,
)

/**
 * Every modal dialog this view can put over the board. The inline card composer
 * is deliberately NOT one: it lives in the lane, does not cover anything, and
 * `n` on an already-composing column is a documented no-op.
 */
const anyDialogOpen = computed(
  () =>
    Boolean(selectedCard.value) || Boolean(editingColumnLive.value) || showBoardSettings.value,
)

watch(anyDialogOpen, (open) => {
  emit('dialog-open-change', open)
})

// A skin switch or a route change unmounts this view outright. Leaving the flag
// stuck at `true` would disable the board shortcuts for the Legacy skin too.
onBeforeUnmount(() => {
  if (anyDialogOpen.value) emit('dialog-open-change', false)
})

function openComposer(column: Column) {
  // A different column's failure is not this column's failure.
  if (composerColumnId.value !== column.id) {
    composerError.value = null
  }
  composerColumnId.value = column.id
}

function cancelComposer() {
  composerColumnId.value = null
  composerError.value = null
}

async function createCardInColumn(column: Column, title: string) {
  if (composerBusy.value) return

  composerBusy.value = true
  composerError.value = null
  try {
    await boardStore.createCard(boardId.value, { columnId: column.id, title })
    // Parity with the Legacy inline form: a successful add closes the composer.
    composerColumnId.value = null
  } catch (error) {
    logError('Failed to create card (paper):', error)
    composerError.value = t('boardDetail.card.error')
  } finally {
    composerBusy.value = false
  }
}

/**
 * Keyboard/pointer column reorder, alongside the existing drag handle. Drag is
 * the only reorder Legacy offers; it is unusable without a pointer, so Paper
 * adds explicit controls over the same `reorderColumns` action.
 */
async function moveColumn(column: Column, direction: 'left' | 'right') {
  const columns = sortedColumns.value
  const index = columns.findIndex((c) => c.id === column.id)
  const targetIndex = direction === 'left' ? index - 1 : index + 1
  if (index === -1 || targetIndex < 0 || targetIndex >= columns.length) return

  const reordered = [...columns]
  const [removed] = reordered.splice(index, 1)
  if (!removed) return
  reordered.splice(targetIndex, 0, removed)

  try {
    await boardStore.reorderColumns(
      boardId.value,
      reordered.map((c) => c.id),
    )
  } catch (error) {
    logError('Failed to reorder columns (paper):', error)
  }
}

function openColumnSettings(column: Column) {
  editingColumn.value = column
}

function closeColumnSettings() {
  editingColumn.value = null
}

function openBoardSettings() {
  showBoardSettings.value = true
}

function closeBoardSettings() {
  showBoardSettings.value = false
}

/**
 * Empty-board bootstrap (#1765).
 *
 * A board created from the Boards list starts with zero columns, and the paper
 * board surface previously offered no way out of that state. Both actions below
 * reuse `boardStore.createColumn` — the same path the legacy toolbar form uses —
 * so persistence, toasts and realtime behaviour are unchanged.
 */
const STARTER_COLUMN_NAMES = ['To Do', 'In Progress', 'Done'] as const

const firstColumnName = ref('')
const creatingColumns = ref(false)
const columnError = ref<string | null>(null)

/**
 * A loaded board that has no columns. The empty state takes precedence over the
 * generic board error banner here so a failed column create keeps the recovery
 * affordance on screen instead of replacing it with a bare error.
 */
const isEmptyBoard = computed(() => Boolean(boardStore.currentBoard) && sortedColumns.value.length === 0)

const emptyStateError = computed(() => columnError.value ?? boardStore.error)

const canSubmitFirstColumn = computed(
  () => firstColumnName.value.trim().length > 0 && !creatingColumns.value,
)

async function createFirstColumn() {
  const name = firstColumnName.value.trim()
  if (!name || creatingColumns.value) return

  creatingColumns.value = true
  columnError.value = null
  try {
    await boardStore.createColumn(boardId.value, { name })
    firstColumnName.value = ''
  } catch (error) {
    logError('Failed to create first column (paper):', error)
    columnError.value = 'Could not create the column. Please try again.'
  } finally {
    creatingColumns.value = false
  }
}

/**
 * Add a column to a board that already has some (GH-1959, horizon finding H-03).
 *
 * The empty state above is a first-run bootstrap and only exists at zero
 * columns, so it was the ONLY add-column door on the whole surface — a board
 * was permanently capped at the lanes it happened to start with. This is the
 * ordinary door, at the end of the lane rail, over the same
 * `boardStore.createColumn` action.
 *
 * Deliberately secondary: the primary act on a board is adding a CARD. Position
 * is omitted so the server appends, exactly as the Legacy toolbar form does.
 */
const addColumnOpen = ref(false)
const newColumnName = ref('')
const addColumnError = ref<string | null>(null)

const canSubmitNewColumn = computed(
  () => newColumnName.value.trim().length > 0 && !creatingColumns.value,
)

function openAddColumn() {
  addColumnError.value = null
  addColumnOpen.value = true
}

function cancelAddColumn() {
  addColumnOpen.value = false
  newColumnName.value = ''
  addColumnError.value = null
}

async function createColumnAtEnd() {
  const name = newColumnName.value.trim()
  // A whitespace-only name is a no-op, never a request the server has to reject.
  if (!name || creatingColumns.value) return

  creatingColumns.value = true
  addColumnError.value = null
  try {
    await boardStore.createColumn(boardId.value, { name })
    newColumnName.value = ''
    addColumnOpen.value = false
  } catch (error) {
    logError('Failed to create column (paper):', error)
    addColumnError.value = t('boardDetail.column.addError')
  } finally {
    creatingColumns.value = false
  }
}

async function addStarterColumns() {
  if (creatingColumns.value) return

  creatingColumns.value = true
  columnError.value = null
  try {
    for (const [index, name] of STARTER_COLUMN_NAMES.entries()) {
      await boardStore.createColumn(boardId.value, { name, position: index })
    }
  } catch (error) {
    logError('Failed to create starter columns (paper):', error)
    columnError.value = 'Could not create the starter columns. Please try again.'
  } finally {
    creatingColumns.value = false
  }
}
</script>

<template>
  <div class="paper-board-view" data-surface="paper-board" :data-density="density">
    <div class="paper-board-view__inner">
      <header class="paper-board-view__head">
        <div class="paper-board-view__title-block">
          <span class="paper-board-view__eyebrow tk-eyebrow">Board</span>
          <h1 class="paper-board-view__title tk-h2">
            {{ boardStore.currentBoard?.name ?? 'Board' }}
          </h1>
          <p class="paper-board-view__subline tk-meta">
            {{ totalCards }} cards · {{ sortedColumns.length }} columns
          </p>
        </div>
        <div class="paper-board-view__actions">
          <PaperHLBtn
            label="Compact density"
            :aria-pressed="density === 'compact'"
            data-testid="paper-board-density-toggle"
            @keydown.enter.stop
            @keydown.space.stop
            @click="toggleDensity"
          />
          <PaperHLBtn
            v-if="boardStore.currentBoard"
            :label="t('boardDetail.actions.settings')"
            data-testid="paper-board-settings"
            @click="openBoardSettings"
          />
          <PaperHLBtn label="Capture here" kbd="C" @click="openCaptureBoard" />
          <PaperHLBtn variant="ember" label="Review" kbd="R" @click="openReview" />
        </div>
      </header>

      <!--
        The banner reports; it does not replace. It used to head the same
        `v-if` chain as the lanes, so ANY store error — including a rejected
        direct add-card, which sets `state.error` before it rethrows — unmounted
        every lane and took the user's half-typed draft with it (GH-1959). It
        now renders above whatever follows. The empty state still wins where it
        applies: it owns `emptyStateError`, so `!isEmptyBoard` keeps the banner
        out of its way.
      -->
      <section
        v-if="boardStore.error && !isEmptyBoard"
        class="paper-board-view__error"
        role="alert"
      >
        {{ boardStore.error }}
      </section>

      <section
        v-if="!boardStore.currentBoard && boardStore.loading"
        class="paper-board-view__loading"
        aria-live="polite"
      >
        Loading board…
      </section>

      <section
        v-else-if="isEmptyBoard"
        class="paper-board-view__empty"
        data-testid="paper-board-empty"
      >
        <p class="paper-board-view__empty-lead tk-meta">— no columns yet —</p>
        <p class="paper-board-view__empty-copy">
          Columns are the lanes work moves through. Add the first one to make this board usable.
        </p>

        <form class="paper-board-view__empty-form" @submit.prevent="createFirstColumn">
          <label class="sr-only" for="paper-board-first-column-name">Column name</label>
          <input
            id="paper-board-first-column-name"
            v-model="firstColumnName"
            type="text"
            class="paper-board-view__empty-input"
            placeholder="Column name"
            :disabled="creatingColumns"
            data-testid="paper-board-empty-column-name"
          />
          <PaperHLBtn
            type="submit"
            variant="primary"
            label="Add first column"
            :disabled="!canSubmitFirstColumn"
            data-testid="paper-board-empty-add-column"
          />
        </form>

        <div class="paper-board-view__empty-alt">
          <PaperHLBtn
            label="Add starter columns (To Do · In Progress · Done)"
            :disabled="creatingColumns"
            data-testid="paper-board-empty-starter-columns"
            @click="addStarterColumns"
          />
        </div>

        <p
          v-if="emptyStateError"
          class="paper-board-view__empty-error"
          role="alert"
          data-testid="paper-board-empty-error"
        >
          {{ emptyStateError }}
        </p>
      </section>

      <!--
        `v-else-if` rather than a bare `v-else`: with the banner lifted out of
        this chain, a board that failed to load (no `currentBoard`) must render
        neither the column-bootstrap empty state nor an empty lane rail.
      -->
      <div
        v-else-if="boardStore.currentBoard"
        class="paper-board-view__workspace"
        data-testid="paper-board-workspace"
      >
        <div
          class="paper-board-view__lanes"
          :class="{ 'paper-board-view__lanes--snap': viewportMode === 'tablet' }"
          data-testid="paper-board-lanes"
        >
        <!-- eslint-disable-next-line vuejs-accessibility/no-static-element-interactions -- column drag/drop wrapper; group role + drag events drive existing reorder semantics -->
        <div
          v-for="(column, idx) in sortedColumns"
          :key="column.id"
          class="paper-board-view__lane"
          :data-column-dnd-id="column.id"
          :class="{
            'paper-board-view__lane--dragging': draggedColumn?.id === column.id,
            'paper-board-view__lane--drop-target': dragOverColumnId === column.id,
          }"
          @dragstart="onLaneDragStart(column, $event)"
          @dragend="handleColumnDragEnd"
          @dragover="(event) => { handleColumnDragOver(column, event); onCardDragOverColumn(column, event) }"
          @dragleave="handleColumnDragLeave"
          @drop="(event) => { handleColumnDrop(column, event); if (draggedCard) onCardDropOnColumn(column, event) }"
        >
          <PaperBoardColumn
            :column="column"
            :index="idx + 1"
            :cards="cardsByColumn.get(column.id) ?? []"
            :card-variant="props.cardVariant"
            :is-drag-over="dragOverColumnId === column.id"
            :selected-card-id="activeSelectedCardId"
            :can-move-left="idx > 0"
            :can-move-right="idx < sortedColumns.length - 1"
            :composer-open="composerColumnId === column.id"
            :composer-busy="composerBusy"
            :composer-error="composerError"
            @capture="openCapture"
            @edit="openColumnSettings"
            @move="moveColumn"
            @open-composer="openComposer"
            @submit-card="createCardInColumn"
            @cancel-composer="cancelComposer"
            @card-click="openCard"
            @card-dragstart="(card) => handleCardDragStart(card)"
            @card-dragend="handleCardDragEnd"
            @card-dragover="(_card, event) => onCardDragOverCard(event)"
            @card-drop="onCardDropOnCard"
          />
        </div>

          <div class="paper-board-view__add-column" data-testid="paper-board-add-column-cell">
          <PaperHLBtn
            v-if="!addColumnOpen"
            :label="t('boardDetail.column.add')"
            :aria-label="t('boardDetail.column.addAria')"
            data-testid="paper-board-add-column"
            @click="openAddColumn"
          />

          <form
            v-else
            class="paper-board-view__add-column-form"
            data-testid="paper-board-add-column-form"
            @submit.prevent="createColumnAtEnd"
          >
            <label class="sr-only" for="paper-board-add-column-name">
              {{ t('boardDetail.column.addInputLabel') }}
            </label>
            <input
              id="paper-board-add-column-name"
              v-model="newColumnName"
              type="text"
              class="paper-board-view__add-column-input"
              :placeholder="t('boardDetail.column.addPlaceholder')"
              :disabled="creatingColumns"
              data-testid="paper-board-add-column-name"
              @keydown.esc.stop.prevent="cancelAddColumn"
            />

            <div class="paper-board-view__add-column-actions">
              <PaperHLBtn
                type="submit"
                variant="primary"
                :label="t('boardDetail.column.addSubmit')"
                :disabled="!canSubmitNewColumn"
                data-testid="paper-board-add-column-submit"
              />
              <PaperHLBtn
                type="button"
                variant="ghost"
                :label="t('boardDetail.column.addCancel')"
                data-testid="paper-board-add-column-cancel"
                @click="cancelAddColumn"
              />
            </div>

            <p
              v-if="addColumnError"
              class="paper-board-view__add-column-error"
              role="alert"
              data-testid="paper-board-add-column-error"
            >
              {{ addColumnError }}
            </p>
          </form>
          </div>
        </div>

        <CardModal
          v-if="selectedCard"
          :card="selectedCard"
          :is-open="Boolean(selectedCard)"
          :labels="boardStore.currentBoardLabels"
          :presentation="cardPresentation"
          @close="closeCard"
          @updated="closeCard"
          @dirty-change="handleCardEditorDirtyChange"
        />

        <TdDialog
          v-if="pendingCard"
          :open="true"
          title="Discard card changes?"
          :description="`Switch to ${pendingCard.title} and discard the current unsaved changes?`"
          @close="cancelCardSwitch"
        >
          <template #footer>
            <PaperHLBtn
              type="button"
              variant="ghost"
              label="Keep editing"
              data-testid="card-switch-cancel"
              @click="cancelCardSwitch"
            />
            <PaperHLBtn
              type="button"
              variant="primary"
              label="Discard and switch"
              data-testid="card-switch-confirm"
              @click="discardAndSwitchCard"
            />
          </template>
        </TdDialog>
      </div>

      <PaperColumnSettingsDialog
        v-if="editingColumnLive"
        :column="editingColumnLive"
        :board-id="boardId"
        :is-open="Boolean(editingColumnLive)"
        :card-count="editingColumnCardCount"
        @close="closeColumnSettings"
        @updated="closeColumnSettings"
      />

      <PaperBoardSettingsDialog
        v-if="boardStore.currentBoard && showBoardSettings"
        :board="boardStore.currentBoard"
        :is-open="showBoardSettings"
        @close="closeBoardSettings"
        @updated="closeBoardSettings"
      />
    </div>
  </div>
</template>

<style scoped>
.paper-board-view {
  min-height: 100%;
  background: var(--paper);
  color: var(--ink);
}

.paper-board-view__inner {
  padding: 24px 32px 32px;
  display: flex;
  flex-direction: column;
  gap: 18px;
}

.paper-board-view__head {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: 24px;
}

.paper-board-view__title-block {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}

.paper-board-view__title {
  margin: 0;
}

.paper-board-view__subline {
  margin: 0;
}

.paper-board-view__actions {
  display: flex;
  gap: 8px;
  flex: none;
}

.paper-board-view__error {
  border: 1px solid var(--ember);
  background: var(--ember-tint);
  color: var(--ember-ink);
  padding: 10px 14px;
  border-radius: var(--r-2);
  font-family: var(--mono);
  font-size: 11px;
}

.paper-board-view__loading,
.paper-board-view__empty {
  padding: 24px;
  text-align: center;
  border: 1px dashed var(--line-soft);
  border-radius: var(--r-2);
  background: var(--paper-2);
}

.paper-board-view__empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 12px;
}

.paper-board-view__empty-lead {
  margin: 0;
}

.paper-board-view__empty-copy {
  margin: 0;
  max-width: 46ch;
  font-family: var(--serif);
  font-size: 14px;
  color: var(--ink);
}

.paper-board-view__empty-form {
  display: flex;
  align-items: stretch;
  gap: 8px;
  flex-wrap: wrap;
  justify-content: center;
}

.paper-board-view__empty-input {
  min-width: 200px;
  padding: 6px 10px;
  border: 1px solid var(--line-soft);
  border-radius: var(--r-2);
  background: var(--paper);
  color: var(--ink);
  font-family: var(--serif);
  font-size: 14px;
}

.paper-board-view__empty-input::placeholder {
  font-family: var(--serif);
  font-style: italic;
  color: var(--mute);
}

.paper-board-view__empty-input:disabled {
  opacity: 0.6;
  cursor: progress;
}

.paper-board-view__empty-alt {
  display: flex;
  justify-content: center;
}

.paper-board-view__empty-error {
  margin: 0;
  color: var(--ember-ink);
  font-family: var(--mono);
  font-size: 11px;
}

.paper-board-view__lanes {
  min-width: 0;
  flex: 1 1 auto;
  display: flex;
  flex-direction: row;
  align-items: flex-start;
  gap: 16px;
  overflow-x: auto;
  padding-bottom: 8px;
}

.paper-board-view__workspace {
  display: flex;
  align-items: flex-start;
  gap: 18px;
  min-width: 0;
}

.paper-board-view[data-density="compact"] .paper-board-view__inner {
  gap: 12px;
  padding: 16px 20px 20px;
}

.paper-board-view[data-density="compact"] .paper-board-view__lanes {
  gap: 10px;
}

.paper-board-view[data-density="compact"] :deep(.paper-board-column) {
  gap: 6px;
  padding: 8px;
}

.paper-board-view[data-density="compact"] :deep(.paper-board-column__cards),
.paper-board-view[data-density="compact"] :deep(.paper-board-column__footer) {
  gap: 5px;
}

.paper-board-view[data-density="compact"] :deep(.paper-board-card__body) {
  padding: 8px 10px;
}

.paper-board-view[data-density="compact"] :deep(.paper-board-card__meta) {
  margin-top: 4px;
  padding-top: 4px;
}

.paper-board-view__lane {
  display: contents;
}

/* Clearly secondary to the lanes: narrower, dashed, no card surface. Adding a
 * CARD is the primary act on a board; adding a lane is occasional. */
.paper-board-view__add-column {
  flex: 0 0 200px;
  display: flex;
  flex-direction: column;
  gap: 8px;
  align-self: flex-start;
  padding: 12px;
  border: 1px dashed var(--line-soft);
  border-radius: var(--r-2);
  background: transparent;
}

.paper-board-view__add-column-form {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.paper-board-view__add-column-input {
  width: 100%;
  padding: 6px 8px;
  border: 1px solid var(--line-soft);
  border-radius: var(--r-1);
  background: var(--paper);
  color: var(--ink);
  font-family: var(--serif);
  font-size: 14px;
}

.paper-board-view__add-column-input::placeholder {
  font-family: var(--serif);
  font-style: italic;
  color: var(--mute);
}

.paper-board-view__add-column-input:disabled {
  opacity: 0.6;
  cursor: progress;
}

.paper-board-view__add-column-actions {
  display: flex;
  gap: 6px;
}

.paper-board-view__add-column-error {
  margin: 0;
  color: var(--ember-ink);
  font-family: var(--mono);
  font-size: 10.5px;
}

.paper-board-view__lane > * {
  /* When the wrapper is `display: contents` only the column itself receives
   * layout. Drag-target / dragging affordances are surfaced via the column
   * border in PaperBoardColumn. */
}

.paper-board-view__lanes--snap {
  scroll-snap-type: x mandatory;
  -webkit-overflow-scrolling: touch;
}

.paper-board-view__lanes--snap .paper-board-view__lane {
  display: block;
  scroll-snap-align: start;
  flex: 0 0 280px;
}

@media (max-width: 640px) {
  .paper-board-view__inner {
    padding: 16px;
  }
  .paper-board-view__lanes:not(.paper-board-view__lanes--snap) {
    flex-direction: column;
    overflow-x: visible;
  }
  .paper-board-view__lanes:not(.paper-board-view__lanes--snap) .paper-board-view__lane {
    display: block;
  }
}

@media (max-width: 1024px) {
  .paper-board-view__workspace {
    display: block;
  }
}

@media (prefers-reduced-motion: reduce) {
  .paper-board-view__lanes--snap {
    scroll-snap-type: none;
  }
}
</style>
