<script setup lang="ts">
import { computed, ref, watch } from 'vue'
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

const route = useRoute()
const router = useRouter()
const boardStore = useBoardStore()
const { t } = useI18n()
const { mode: viewportMode } = useViewportMode()

const boardId = computed(() => (typeof route.params.id === 'string' ? route.params.id : ''))
const selectedCard = ref<Card | null>(null)

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
  selectedCard.value = card
}

function closeCard() {
  selectedCard.value = null
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
  <div class="paper-board-view" data-surface="paper-board">
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
            v-if="boardStore.currentBoard"
            :label="t('boardDetail.actions.settings')"
            data-testid="paper-board-settings"
            @click="openBoardSettings"
          />
          <PaperHLBtn label="Capture here" kbd="C" @click="openCaptureBoard" />
          <PaperHLBtn variant="ember" label="Review" kbd="R" @click="openReview" />
        </div>
      </header>

      <section
        v-if="boardStore.error && !isEmptyBoard"
        class="paper-board-view__error"
        role="alert"
      >
        {{ boardStore.error }}
      </section>

      <section
        v-else-if="!boardStore.currentBoard && boardStore.loading"
        class="paper-board-view__loading"
        aria-live="polite"
      >
        Loading board…
      </section>

      <section
        v-else-if="sortedColumns.length === 0"
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

      <div
        v-else
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
      </div>

      <CardModal
        v-if="selectedCard"
        :card="selectedCard"
        :is-open="Boolean(selectedCard)"
        :labels="boardStore.currentBoardLabels"
        @close="closeCard"
        @updated="closeCard"
      />

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
  display: flex;
  flex-direction: row;
  align-items: flex-start;
  gap: 16px;
  overflow-x: auto;
  padding-bottom: 8px;
}

.paper-board-view__lane {
  display: contents;
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

@media (prefers-reduced-motion: reduce) {
  .paper-board-view__lanes--snap {
    scroll-snap-type: none;
  }
}
</style>
