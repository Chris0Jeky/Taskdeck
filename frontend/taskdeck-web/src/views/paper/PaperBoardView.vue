<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useBoardStore } from '../../store/boardStore'
import { useBoardDragDrop } from '../../composables/useBoardDragDrop'
import PaperBoardColumn from './PaperBoardColumn.vue'
import PaperHLBtn from '../../components/paper/PaperHLBtn.vue'
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
 */
const props = withDefaults(
  defineProps<{
    /** Card visual variant — propagated to every column. */
    cardVariant?: PaperBoardCardVariant
  }>(),
  { cardVariant: 'index' },
)

const route = useRoute()
const router = useRouter()
const boardStore = useBoardStore()

const boardId = ref((route.params.id as string) ?? '')

const sortedColumns = computed<Column[]>(() => {
  if (!boardStore.currentBoard) return []
  return [...boardStore.currentBoard.columns].sort((a, b) => a.position - b.position)
})

const cardsByColumn = computed<Map<string, Card[]>>(() => boardStore.cardsByColumn)

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

onMounted(async () => {
  if (!boardId.value) return
  try {
    await boardStore.fetchBoard(boardId.value)
  } catch (error) {
    logError('Failed to load board (paper):', error)
  }
})

watch(
  () => route.params.id,
  async (nextId) => {
    const next = typeof nextId === 'string' ? nextId : ''
    if (!next || next === boardId.value) return
    boardId.value = next
    try {
      await boardStore.fetchBoard(boardId.value)
    } catch (error) {
      logError('Failed to switch board (paper):', error)
    }
  },
)

/**
 * Drop a card onto a column — reuses `boardStore.moveCard` so the same
 * audit-log + persistence path runs as the Obsidian view.
 */
async function onCardDropOnColumn(column: Column, event: DragEvent) {
  event.preventDefault()
  if (!draggedCard.value) return
  if (draggedCard.value.columnId === column.id) return
  try {
    const targetCards = cardsByColumn.value.get(column.id) ?? []
    await boardStore.moveCard(boardId.value, draggedCard.value.id, column.id, targetCards.length)
  } catch (error) {
    logError('Failed to move card (paper):', error)
  } finally {
    handleCardDragEnd()
  }
}

function onCardDragOverColumn(event: DragEvent) {
  // Permit cards to be dropped onto columns even if column-reorder logic
  // (which only fires when a column is being dragged) doesn't claim the
  // event. handleColumnDragOver already calls preventDefault when a column
  // is being dragged.
  if (draggedCard.value) {
    event.preventDefault()
    if (event.dataTransfer) event.dataTransfer.dropEffect = 'move'
  }
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
          <PaperHLBtn label="Capture here" kbd="C" @click="openCaptureBoard" />
          <PaperHLBtn variant="ember" label="Review" kbd="R" @click="openReview" />
        </div>
      </header>

      <section
        v-if="boardStore.error"
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
      >
        <p class="tk-meta">— no columns yet —</p>
      </section>

      <div
        v-else
        class="paper-board-view__lanes"
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
          @dragstart="handleColumnDragStart(column, $event)"
          @dragend="handleColumnDragEnd"
          @dragover="(event) => { handleColumnDragOver(column, event); onCardDragOverColumn(event) }"
          @dragleave="handleColumnDragLeave"
          @drop="(event) => { handleColumnDrop(column, event); if (draggedCard) onCardDropOnColumn(column, event) }"
        >
          <PaperBoardColumn
            :column="column"
            :index="idx + 1"
            :cards="cardsByColumn.get(column.id) ?? []"
            :card-variant="props.cardVariant"
            :is-drag-over="dragOverColumnId === column.id"
            @capture="openCapture"
            @card-dragstart="(card) => handleCardDragStart(card)"
            @card-dragend="handleCardDragEnd"
          />
        </div>
      </div>
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

@media (max-width: 640px) {
  .paper-board-view__inner {
    padding: 16px;
  }
  .paper-board-view__lanes {
    flex-direction: column;
    overflow-x: visible;
  }
  .paper-board-view__lane {
    display: block;
  }
}
</style>
