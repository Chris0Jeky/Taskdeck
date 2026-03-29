<script setup lang="ts">
import ColumnLane from './ColumnLane.vue'
import type { Column, Card, Label } from '../../types/board'

defineProps<{
  sortedColumns: Column[]
  cardsByColumn: Map<string, Card[]>
  labels: Label[]
  boardId: string
  hasColumns: boolean
  draggedColumn: Column | null
  dragOverColumnId: string | null
  draggedCard: Card | null
  selectedCardId: string | null
}>()

defineEmits<{
  columnDragStart: [column: Column, event: DragEvent]
  columnDragEnd: []
  columnDragOver: [column: Column, event: DragEvent]
  columnDragLeave: []
  columnDrop: [column: Column, event: DragEvent]
  cardDragStart: [card: Card]
  cardDragEnd: []
}>()
</script>

<template>
  <div class="td-board-canvas">
    <div class="td-board-canvas__lanes">
      <div
        v-for="column in sortedColumns"
        :key="column.id"
        :data-column-dnd-id="column.id"
        draggable="false"
        :class="[
          'transition-all',
          draggedColumn?.id === column.id ? 'opacity-50' : '',
          dragOverColumnId === column.id ? 'transform scale-105' : ''
        ]"
        @dragstart="$emit('columnDragStart', column, $event)"
        @dragend="$emit('columnDragEnd')"
        @dragover="$emit('columnDragOver', column, $event)"
        @dragleave="$emit('columnDragLeave')"
        @drop="$emit('columnDrop', column, $event)"
      >
        <ColumnLane
          :column="column"
          :cards="cardsByColumn.get(column.id) || []"
          :labels="labels"
          :board-id="boardId"
          :dragged-card="draggedCard"
          :selected-card-id="selectedCardId"
          @card-drag-start="$emit('cardDragStart', $event)"
          @card-drag-end="$emit('cardDragEnd')"
        />
      </div>

      <!-- Empty State -->
      <div
        v-if="!hasColumns"
        class="td-board-canvas__empty"
      >
        <svg
          class="td-board-canvas__empty-icon"
          fill="none"
          stroke="currentColor"
          viewBox="0 0 24 24"
        >
          <path
            stroke-linecap="round"
            stroke-linejoin="round"
            stroke-width="2"
            d="M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2"
          />
        </svg>
        <p class="td-board-canvas__empty-title">No columns yet</p>
        <p class="td-board-canvas__empty-hint">Click "Add Column" to get started</p>
      </div>
    </div>
  </div>
</template>

<style scoped>
.td-board-canvas {
  height: calc(100vh - 120px);
  overflow-x: auto;
}

.td-board-canvas__lanes {
  display: flex;
  gap: var(--td-space-5);
  padding: var(--td-space-6);
  min-height: 100%;
}

.td-board-canvas__empty {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  color: var(--td-text-tertiary);
}

.td-board-canvas__empty-icon {
  width: 4rem;
  height: 4rem;
  margin-bottom: var(--td-space-5);
}

.td-board-canvas__empty-title {
  font-size: var(--td-font-lg);
  font-weight: 500;
  color: var(--td-text-tertiary);
}

.td-board-canvas__empty-hint {
  font-size: var(--td-font-sm);
  color: var(--td-text-tertiary);
  margin-top: var(--td-space-1);
}
</style>
