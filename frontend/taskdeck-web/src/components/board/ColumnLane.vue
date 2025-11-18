<script setup lang="ts">
import { ref } from 'vue'
import { useBoardStore } from '../../store/boardStore'
import CardItem from './CardItem.vue'
import CardModal from './CardModal.vue'
import ColumnEditModal from './ColumnEditModal.vue'
import type { Column, Card, Label } from '../../types/board'

const props = defineProps<{
  column: Column
  cards: Card[]
  labels: Label[]
  boardId: string
  draggedCard: Card | null
}>()

const emit = defineEmits<{
  (e: 'card-drag-start', card: Card): void
  (e: 'card-drag-end'): void
}>()

const boardStore = useBoardStore()
const newCardTitle = ref('')
const showCardForm = ref(false)
const selectedCard = ref<Card | null>(null)
const showCardModal = ref(false)
const showColumnEdit = ref(false)
const isDragOver = ref(false)

function handleCardClick(card: Card) {
  selectedCard.value = card
  showCardModal.value = true
}

function handleModalClose() {
  showCardModal.value = false
  selectedCard.value = null
}

async function createCard() {
  if (!newCardTitle.value.trim()) return

  try {
    await boardStore.createCard(props.boardId, {
      columnId: props.column.id,
      title: newCardTitle.value,
    })

    newCardTitle.value = ''
    showCardForm.value = false
  } catch (error) {
    console.error('Failed to create card:', error)
  }
}

const isWipLimitExceeded = () => {
  return props.column.wipLimit !== null && props.cards.length > props.column.wipLimit
}

function handleCardDragStart(card: Card) {
  draggedCard.value = card
}

function handleCardDragEnd() {
  draggedCard.value = null
  isDragOver.value = false
}

function handleDragOver(event: DragEvent) {
  event.preventDefault()
  if (!draggedCard.value) return

  // Don't show drop indicator if card is already in this column
  if (draggedCard.value.columnId === props.column.id) {
    isDragOver.value = false
    return
  }

  isDragOver.value = true
  if (event.dataTransfer) {
    event.dataTransfer.dropEffect = 'move'
  }
}

function handleDragLeave() {
  isDragOver.value = false
}

async function handleDrop(event: DragEvent) {
  event.preventDefault()
  isDragOver.value = false

  if (!draggedCard.value) return

  // Don't move if dropping in the same column
  if (draggedCard.value.columnId === props.column.id) {
    return
  }

  try {
    // Move to end of target column
    const targetPosition = props.cards.length
    await boardStore.moveCard(
      props.boardId,
      draggedCard.value.id,
      props.column.id,
      targetPosition
    )
  } catch (error) {
    console.error('Failed to move card:', error)
  }
}

// Handle drop between cards for reordering within a column or between columns
async function handleCardDrop(targetCard: Card, event: DragEvent) {
  event.preventDefault()
  event.stopPropagation()

  if (!draggedCard.value) return

  // Don't drop on self
  if (draggedCard.value.id === targetCard.id) return

  try {
    // Calculate the target position
    let targetPosition = targetCard.position

    // If dropping in the same column and the dragged card is before the target,
    // we need to account for the removal of the dragged card
    if (draggedCard.value.columnId === props.column.id && draggedCard.value.position < targetCard.position) {
      targetPosition--
    }

    await boardStore.moveCard(
      props.boardId,
      draggedCard.value.id,
      props.column.id,
      targetPosition
    )
  } catch (error) {
    console.error('Failed to move card:', error)
  }
}

function handleCardDragOver(event: DragEvent) {
  event.preventDefault()
  event.stopPropagation()
  if (event.dataTransfer) {
    event.dataTransfer.dropEffect = 'move'
  }
}
</script>

<template>
  <div
    :class="[
      'flex-shrink-0 w-80 rounded-lg p-4 transition-all',
      isDragOver ? 'bg-blue-50 ring-2 ring-blue-400' : 'bg-gray-100'
    ]"
    @dragover="handleDragOver"
    @dragleave="handleDragLeave"
    @drop="handleDrop"
  >
    <!-- Column Header -->
    <div class="mb-4">
      <div class="flex items-center justify-between mb-2">
        <h3 class="font-semibold text-gray-900 flex-1">{{ column.name }}</h3>
        <div class="flex items-center gap-2">
          <span
            class="text-sm px-2 py-1 rounded"
            :class="isWipLimitExceeded() ? 'bg-red-100 text-red-700' : 'bg-gray-200 text-gray-700'"
          >
            {{ cards.length }}{{ column.wipLimit ? `/${column.wipLimit}` : '' }}
          </span>
          <button
            @click="showColumnEdit = true"
            class="p-1 text-gray-500 hover:text-gray-700 hover:bg-gray-200 rounded transition-colors"
            title="Edit Column"
          >
            <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.065 2.572c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.572 1.065c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.065-2.572c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z" />
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
            </svg>
          </button>
        </div>
      </div>

      <div v-if="isWipLimitExceeded()" class="text-xs text-red-600 mb-2">
        ⚠️ WIP limit exceeded
      </div>

      <button
        @click="showCardForm = !showCardForm"
        class="w-full px-3 py-2 text-sm text-gray-700 hover:bg-gray-200 rounded transition-colors flex items-center justify-center gap-1"
      >
        <span>+</span>
        <span>Add Card</span>
      </button>

      <!-- Create Card Form -->
      <div v-if="showCardForm" class="mt-3 bg-white rounded-lg p-3 shadow-sm">
        <form @submit.prevent="createCard">
          <textarea
            v-model="newCardTitle"
            placeholder="Enter card title..."
            class="w-full px-3 py-2 border border-gray-300 rounded resize-none focus:outline-none focus:ring-2 focus:ring-blue-500"
            rows="3"
            autofocus
          ></textarea>
          <div class="flex gap-2 mt-2">
            <button
              type="submit"
              class="px-3 py-1.5 bg-blue-600 text-white text-sm rounded hover:bg-blue-700 transition-colors"
            >
              Add
            </button>
            <button
              type="button"
              @click="showCardForm = false"
              class="px-3 py-1.5 bg-gray-200 text-gray-700 text-sm rounded hover:bg-gray-300 transition-colors"
            >
              Cancel
            </button>
          </div>
        </form>
      </div>
    </div>

    <!-- Cards List -->
    <div class="space-y-2 overflow-y-auto max-h-[calc(100vh-280px)] min-h-[100px]">
      <div
        v-for="card in cards"
        :key="card.id"
        @dragover="handleCardDragOver"
        @drop="handleCardDrop(card, $event)"
      >
        <CardItem
          :card="card"
          @click="handleCardClick"
          @dragstart="handleCardDragStart"
          @dragend="handleCardDragEnd"
        />
      </div>

      <!-- Empty State -->
      <div v-if="cards.length === 0 && !showCardForm" class="text-center py-8 text-gray-400 text-sm">
        No cards yet
      </div>
    </div>

    <!-- Card Modal -->
    <CardModal
      v-if="selectedCard"
      :card="selectedCard"
      :is-open="showCardModal"
      :labels="labels"
      @close="handleModalClose"
      @updated="handleModalClose"
    />

    <!-- Column Edit Modal -->
    <ColumnEditModal
      :column="column"
      :is-open="showColumnEdit"
      :board-id="boardId"
      @close="showColumnEdit = false"
      @updated="() => { showColumnEdit = false }"
    />
  </div>
</template>
