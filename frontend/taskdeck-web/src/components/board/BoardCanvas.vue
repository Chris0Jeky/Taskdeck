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
  <div class="h-[calc(100vh-120px)] overflow-x-auto">
    <div class="flex gap-4 p-6 min-h-full">
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
        class="flex-1 flex flex-col items-center justify-center text-on-surface/20"
      >
        <svg
          class="w-16 h-16 mb-4"
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
        <p class="text-lg font-medium">No columns yet</p>
        <p class="text-sm mt-1">Click "Add Column" to get started</p>
      </div>
    </div>
  </div>
</template>
