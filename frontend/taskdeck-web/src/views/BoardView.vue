<script setup lang="ts">
import { onMounted, ref, computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useBoardStore } from '../store/boardStore'
import { useKeyboardShortcuts } from '../composables/useKeyboardShortcuts'
import ColumnLane from '../components/board/ColumnLane.vue'
import BoardSettingsModal from '../components/board/BoardSettingsModal.vue'
import LabelManagerModal from '../components/board/LabelManagerModal.vue'
import KeyboardShortcutsHelp from '../components/KeyboardShortcutsHelp.vue'
import FilterPanel from '../components/board/FilterPanel.vue'
import type { Column, Card } from '../types/board'
import type { CardFilters } from '../store/boardStore'

const route = useRoute()
const router = useRouter()
const boardStore = useBoardStore()

const newColumnName = ref('')
const showColumnForm = ref(false)
const showBoardSettings = ref(false)
const showLabelManager = ref(false)
const showKeyboardHelp = ref(false)
const showFilterPanel = ref(false)
const draggedColumn = ref<Column | null>(null)
const dragOverColumnId = ref<string | null>(null)
const draggedCard = ref<Card | null>(null)

// Card selection state for keyboard navigation
const selectedCardId = ref<string | null>(null)
const selectedColumnIndex = ref<number>(0)

const boardId = ref(route.params.id as string)

// Sort columns by position
const sortedColumns = computed(() => {
  if (!boardStore.currentBoard) return []
  return [...boardStore.currentBoard.columns].sort((a, b) => a.position - b.position)
})

onMounted(async () => {
  try {
    await boardStore.fetchBoard(boardId.value)
  } catch (error) {
    console.error('Failed to load board:', error)
  }
})

async function createColumn() {
  if (!newColumnName.value.trim()) return

  try {
    await boardStore.createColumn(boardId.value, {
      name: newColumnName.value,
    })
    newColumnName.value = ''
    showColumnForm.value = false
  } catch (error) {
    console.error('Failed to create column:', error)
  }
}

function goBack() {
  router.push('/boards')
}

function handleColumnDragStart(column: Column, event: DragEvent) {
  draggedColumn.value = column
  if (event.dataTransfer) {
    event.dataTransfer.effectAllowed = 'move'
    event.dataTransfer.setData('text/plain', column.id)
  }
}

function handleColumnDragEnd() {
  draggedColumn.value = null
  dragOverColumnId.value = null
}

function handleColumnDragOver(column: Column, event: DragEvent) {
  event.preventDefault()
  if (!draggedColumn.value || draggedColumn.value.id === column.id) {
    dragOverColumnId.value = null
    return
  }
  dragOverColumnId.value = column.id
  if (event.dataTransfer) {
    event.dataTransfer.dropEffect = 'move'
  }
}

function handleColumnDragLeave() {
  dragOverColumnId.value = null
}

async function handleColumnDrop(targetColumn: Column | undefined, event: DragEvent) {
  event.preventDefault()
  dragOverColumnId.value = null

  if (!draggedColumn.value || !boardStore.currentBoard || !targetColumn) return
  if (draggedColumn.value.id === targetColumn.id) return

  try {
    const columns = sortedColumns.value
    const draggedIndex = columns.findIndex((c) => c.id === draggedColumn.value!.id)
    const targetIndex = columns.findIndex((c) => c.id === targetColumn.id)

    if (draggedIndex === -1 || targetIndex === -1) return

    // Reorder locally to get the new order
    const reordered = [...columns]
    const [removed] = reordered.splice(draggedIndex, 1)
    reordered.splice(targetIndex, 0, removed)

    // Use atomic reorder endpoint to update all positions in a single transaction
    const columnIds = reordered.map((col) => col.id)
    await boardStore.reorderColumns(boardId.value, columnIds)
  } catch (error) {
    console.error('Failed to reorder columns:', error)
  }
}

function handleCardDragStart(card: Card) {
  draggedCard.value = card
}

function handleCardDragEnd() {
  draggedCard.value = null
}

// Keyboard navigation functions
function selectNextCard() {
  const columns = sortedColumns.value
  if (columns.length === 0) return

  const currentColumn = columns[selectedColumnIndex.value]
  if (!currentColumn) return

  const cards = boardStore.cardsByColumn.get(currentColumn.id) || []
  if (cards.length === 0) return

  if (!selectedCardId.value) {
    // Select first card in current column
    selectedCardId.value = cards[0]?.id || null
    console.log('Selected first card:', selectedCardId.value)
    return
  }

  const currentIndex = cards.findIndex(c => c.id === selectedCardId.value)
  if (currentIndex < cards.length - 1) {
    selectedCardId.value = cards[currentIndex + 1]?.id || null
    console.log('Selected next card:', selectedCardId.value)
  }
}

function selectPreviousCard() {
  const columns = sortedColumns.value
  if (columns.length === 0) return

  const currentColumn = columns[selectedColumnIndex.value]
  if (!currentColumn) return

  const cards = boardStore.cardsByColumn.get(currentColumn.id) || []
  if (cards.length === 0) return

  if (!selectedCardId.value) {
    // Select last card in current column
    selectedCardId.value = cards[cards.length - 1]?.id || null
    return
  }

  const currentIndex = cards.findIndex(c => c.id === selectedCardId.value)
  if (currentIndex > 0) {
    selectedCardId.value = cards[currentIndex - 1]?.id || null
  }
}

function selectNextColumn() {
  const columns = sortedColumns.value
  if (columns.length === 0) return

  if (selectedColumnIndex.value < columns.length - 1) {
    selectedColumnIndex.value++
    // Select first card in new column
    const newColumn = columns[selectedColumnIndex.value]
    if (newColumn) {
      const cards = boardStore.cardsByColumn.get(newColumn.id) || []
      selectedCardId.value = cards.length > 0 ? (cards[0]?.id || null) : null
    }
  }
}

function selectPreviousColumn() {
  const columns = sortedColumns.value
  if (columns.length === 0) return

  if (selectedColumnIndex.value > 0) {
    selectedColumnIndex.value--
    // Select first card in new column
    const newColumn = columns[selectedColumnIndex.value]
    if (newColumn) {
      const cards = boardStore.cardsByColumn.get(newColumn.id) || []
      selectedCardId.value = cards.length > 0 ? (cards[0]?.id || null) : null
    }
  }
}

function openSelectedCard() {
  if (!selectedCardId.value) return
  const columns = sortedColumns.value
  const currentColumn = columns[selectedColumnIndex.value]
  if (!currentColumn) return

  const cards = boardStore.cardsByColumn.get(currentColumn.id) || []
  const card = cards.find(c => c.id === selectedCardId.value)
  if (card) {
    // TODO: Open card modal - will be implemented when integrating
    console.log('Opening card:', card)
  }
}

function createCardInSelectedColumn() {
  const columns = sortedColumns.value
  if (columns.length === 0) return
  const currentColumn = columns[selectedColumnIndex.value]
  if (!currentColumn) return

  // TODO: Open create card form - will be implemented when integrating
  console.log('Creating card in column:', currentColumn.name)
}

function toggleKeyboardHelp() {
  showKeyboardHelp.value = !showKeyboardHelp.value
}

function toggleFilterPanel() {
  showFilterPanel.value = !showFilterPanel.value
}

function handleFiltersUpdate(newFilters: CardFilters) {
  boardStore.updateFilters(newFilters)
}

// Setup keyboard shortcuts
useKeyboardShortcuts([
  // Navigation
  { key: 'j', description: 'Next card', action: selectNextCard },
  { key: 'ArrowDown', description: 'Next card', action: selectNextCard },
  { key: 'k', description: 'Previous card', action: selectPreviousCard },
  { key: 'ArrowUp', description: 'Previous card', action: selectPreviousCard },
  { key: 'h', description: 'Previous column', action: selectPreviousColumn },
  { key: 'ArrowLeft', description: 'Previous column', action: selectPreviousColumn },
  { key: 'l', description: 'Next column', action: selectNextColumn },
  { key: 'ArrowRight', description: 'Next column', action: selectNextColumn },

  // Actions
  { key: 'Enter', description: 'Open selected card', action: openSelectedCard },
  { key: 'n', description: 'New card in current column', action: createCardInSelectedColumn },

  // Help
  { key: '?', description: 'Toggle keyboard shortcuts help', action: toggleKeyboardHelp },
  { key: 'f', description: 'Toggle filter panel', action: toggleFilterPanel },
])
</script>

<template>
  <div class="min-h-screen bg-gray-50">
    <!-- Header -->
    <div class="bg-white border-b border-gray-200">
      <div class="max-w-full px-4 sm:px-6 lg:px-8 py-4">
        <div class="flex items-center justify-between">
          <div class="flex items-center gap-4">
            <button
              @click="goBack"
              class="text-gray-600 hover:text-gray-900 transition-colors"
            >
              <svg class="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path
                  stroke-linecap="round"
                  stroke-linejoin="round"
                  stroke-width="2"
                  d="M15 19l-7-7 7-7"
                />
              </svg>
            </button>
            <div v-if="boardStore.currentBoard">
              <h1 class="text-2xl font-bold text-gray-900">
                {{ boardStore.currentBoard.name }}
              </h1>
              <p v-if="boardStore.currentBoard.description" class="text-sm text-gray-600">
                {{ boardStore.currentBoard.description }}
              </p>
            </div>
          </div>

          <div class="flex items-center gap-2">
            <button
              @click="toggleFilterPanel"
              :class="[
                'p-2 border border-gray-300 rounded-lg transition-colors',
                showFilterPanel ? 'bg-blue-100 text-blue-700 border-blue-400' : 'text-gray-600 hover:bg-gray-100'
              ]"
              title="Filter Cards (Press f)"
            >
              <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M3 4a1 1 0 011-1h16a1 1 0 011 1v2.586a1 1 0 01-.293.707l-6.414 6.414a1 1 0 00-.293.707V17l-4 4v-6.586a1 1 0 00-.293-.707L3.293 7.293A1 1 0 013 6.586V4z" />
              </svg>
              <span v-if="boardStore.filteredCardCount < boardStore.totalCardCount" class="absolute -top-1 -right-1 flex h-5 w-5 items-center justify-center rounded-full bg-blue-600 text-[10px] font-bold text-white">
                {{ boardStore.filteredCardCount }}
              </span>
            </button>
            <button
              @click="showKeyboardHelp = true"
              class="p-2 text-gray-600 hover:bg-gray-100 border border-gray-300 rounded-lg transition-colors"
              title="Keyboard Shortcuts (Press ?)"
            >
              <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M8.228 9c.549-1.165 2.03-2 3.772-2 2.21 0 4 1.343 4 3 0 1.4-1.278 2.575-3.006 2.907-.542.104-.994.54-.994 1.093m0 3h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
              </svg>
            </button>
            <button
              @click="showLabelManager = true"
              class="px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-100 border border-gray-300 rounded-lg transition-colors"
              title="Manage Labels"
            >
              <svg class="w-5 h-5 inline-block mr-1" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M7 7h.01M7 3h5c.512 0 1.024.195 1.414.586l7 7a2 2 0 010 2.828l-7 7a2 2 0 01-2.828 0l-7-7A1.994 1.994 0 013 12V7a4 4 0 014-4z" />
              </svg>
              Labels
            </button>
            <button
              @click="showBoardSettings = true"
              class="px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-100 border border-gray-300 rounded-lg transition-colors"
              title="Board Settings"
            >
              <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.065 2.572c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.572 1.065c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.065-2.572c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z" />
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
              </svg>
            </button>
            <button
              @click="showColumnForm = !showColumnForm"
              class="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition-colors"
            >
              + Add Column
            </button>
          </div>
        </div>

        <!-- Create Column Form -->
        <div v-if="showColumnForm" class="mt-4 bg-gray-50 rounded-lg p-4">
          <form @submit.prevent="createColumn" class="flex gap-3">
            <input
              v-model="newColumnName"
              type="text"
              placeholder="Column name"
              class="flex-1 px-4 py-2 border border-gray-300 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500"
              autofocus
            />
            <button
              type="submit"
              class="px-6 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition-colors"
            >
              Create
            </button>
            <button
              type="button"
              @click="showColumnForm = false"
              class="px-6 py-2 bg-gray-200 text-gray-700 rounded-lg hover:bg-gray-300 transition-colors"
            >
              Cancel
            </button>
          </form>
        </div>
      </div>
    </div>

    <!-- Filter Panel -->
    <FilterPanel
      :is-open="showFilterPanel"
      :labels="boardStore.currentBoardLabels"
      :active-filters="boardStore.filters"
      @update:filters="handleFiltersUpdate"
      @toggle="toggleFilterPanel"
    />

    <!-- Loading State -->
    <div v-if="boardStore.loading && !boardStore.currentBoard" class="flex justify-center items-center py-12">
      <div class="inline-block animate-spin rounded-full h-12 w-12 border-b-2 border-blue-600"></div>
    </div>

    <!-- Error State -->
    <div v-else-if="boardStore.error" class="max-w-7xl mx-auto px-4 py-8">
      <div class="bg-red-50 border border-red-200 rounded-lg p-4 text-red-700">
        {{ boardStore.error }}
      </div>
    </div>

    <!-- Board Content -->
    <div v-else-if="boardStore.currentBoard" class="h-[calc(100vh-120px)] overflow-x-auto">
      <div class="flex gap-4 p-6 min-h-full">
        <div
          v-for="column in sortedColumns"
          :key="column.id"
          draggable="true"
          :class="[
            'transition-all',
            draggedColumn?.id === column.id ? 'opacity-50' : '',
            dragOverColumnId === column.id ? 'transform scale-105' : ''
          ]"
          @dragstart="handleColumnDragStart(column, $event)"
          @dragend="handleColumnDragEnd"
          @dragover="handleColumnDragOver(column, $event)"
          @dragleave="handleColumnDragLeave"
          @drop="handleColumnDrop(column, $event)"
        >
          <ColumnLane
            :column="column"
            :cards="boardStore.cardsByColumn.get(column.id) || []"
            :labels="boardStore.currentBoardLabels"
            :board-id="boardId"
            :dragged-card="draggedCard"
            :selected-card-id="selectedCardId"
            @card-drag-start="handleCardDragStart"
            @card-drag-end="handleCardDragEnd"
          />
        </div>

        <!-- Empty State -->
        <div
          v-if="boardStore.currentBoard.columns.length === 0"
          class="flex-1 flex flex-col items-center justify-center text-gray-400"
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

    <!-- Board Settings Modal -->
    <BoardSettingsModal
      v-if="boardStore.currentBoard"
      :board="boardStore.currentBoard"
      :is-open="showBoardSettings"
      @close="showBoardSettings = false"
      @updated="() => { showBoardSettings = false }"
    />

    <!-- Label Manager Modal -->
    <LabelManagerModal
      :is-open="showLabelManager"
      :board-id="boardId"
      :labels="boardStore.currentBoardLabels"
      @close="showLabelManager = false"
      @updated="() => {}"
    />

    <!-- Keyboard Shortcuts Help -->
    <KeyboardShortcutsHelp
      :is-open="showKeyboardHelp"
      @close="showKeyboardHelp = false"
    />
  </div>
</template>
