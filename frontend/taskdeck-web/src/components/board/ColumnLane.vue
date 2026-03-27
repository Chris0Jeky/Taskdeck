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
  selectedCardId?: string | null
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

function openCardForm() {
  showCardForm.value = true
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
  emit('card-drag-start', card)
}

function handleCardDragEnd() {
  emit('card-drag-end')
  isDragOver.value = false
}

function handleDragOver(event: DragEvent) {
  event.preventDefault()
  if (!props.draggedCard) return

  // Don't show drop indicator if card is already in this column
  if (props.draggedCard.columnId === props.column.id) {
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

  if (!props.draggedCard) return

  // Don't move if dropping in the same column
  if (props.draggedCard.columnId === props.column.id) {
    return
  }

  try {
    // Move to end of target column
    const targetPosition = props.cards.length
    await boardStore.moveCard(
      props.boardId,
      props.draggedCard.id,
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

  if (!props.draggedCard) return

  // Don't drop on self
  if (props.draggedCard.id === targetCard.id) return

  try {
    // Calculate the target position
    let targetPosition = targetCard.position

    // If dropping in the same column and the dragged card is before the target,
    // we need to account for the removal of the dragged card
    if (props.draggedCard.columnId === props.column.id && props.draggedCard.position < targetCard.position) {
      targetPosition--
    }

    await boardStore.moveCard(
      props.boardId,
      props.draggedCard.id,
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
    :data-column-id="column.id"
    :class="[
      'flex-shrink-0 w-80 rounded-lg p-4 transition-all',
      isDragOver ? 'bg-[#ff5352]/10 ring-2 ring-[#ff5352]' : 'bg-[#1c1b1b]'
    ]"
    @dragover="handleDragOver"
    @dragleave="handleDragLeave"
    @drop="handleDrop"
  >
    <!-- Column Header -->
    <div class="mb-4">
      <div class="flex items-center justify-between mb-2">
        <h3 class="flex items-center gap-2 font-semibold text-[#e5e2e1] flex-1 font-[Space_Grotesk] text-[11px] uppercase tracking-[0.2em]"><span class="w-1 h-4 rounded-full bg-[#ff5352]"></span>{{ column.name }}</h3>
        <div class="flex items-center gap-2">
          <button
            type="button"
            data-action="drag-column-handle"
            draggable="true"
            class="p-1 text-[#e5e2e1]/60 hover:text-[#e5e2e1]/70 hover:bg-[#3a3939] rounded transition-colors cursor-grab active:cursor-grabbing"
            title="Drag Column"
            aria-label="Drag Column"
            @click.stop
          >
            <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M8 6h.01M8 12h.01M8 18h.01M16 6h.01M16 12h.01M16 18h.01" />
            </svg>
          </button>
          <span
            class="text-sm px-2 py-1 rounded"
            :class="isWipLimitExceeded() ? 'bg-[#ff4d4d]/10 text-[#ff4d4d]' : 'bg-[#3a3939] text-[#e5e2e1]/70'"
          >
            {{ cards.length }}{{ column.wipLimit ? `/${column.wipLimit}` : '' }}
          </span>
          <button
            @click="showColumnEdit = true"
            class="p-1 text-[#e5e2e1]/60 hover:text-[#e5e2e1]/70 hover:bg-[#3a3939] rounded transition-colors"
            title="Edit Column"
          >
            <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.065 2.572c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.572 1.065c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.065-2.572c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z" />
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
            </svg>
          </button>
        </div>
      </div>

      <div v-if="isWipLimitExceeded()" class="text-xs text-[#ff4d4d] mb-2">
        ⚠️ WIP limit exceeded
      </div>

      <button
        data-action="toggle-add-card"
        @click="openCardForm"
        class="w-full px-3 py-2 text-sm text-[#e5e2e1]/60 border border-dashed border-[rgba(91,64,62,0.15)] hover:border-[#ff5352] hover:text-[#ffb3ae] rounded transition-colors flex items-center justify-center gap-1"
      >
        <span>+</span>
        <span>Add Card</span>
      </button>

      <!-- Create Card Form -->
      <div
        v-if="showCardForm"
        data-action="add-card-form"
        class="mt-3 bg-[#201f1f] rounded-lg p-3 shadow-[0_2px_8px_rgba(0,0,0,0.3)]"
      >
        <form @submit.prevent="createCard">
          <textarea
            data-action="add-card-input"
            v-model="newCardTitle"
            placeholder="Enter card title..."
            class="w-full px-3 py-2 bg-[#1c1b1b] border border-[rgba(91,64,62,0.15)] text-[#e5e2e1] rounded resize-none focus:outline-none focus:ring-2 focus:ring-[#ff5352] placeholder:text-[#e5e2e1]/40"
            rows="3"
            autofocus
          ></textarea>
          <div class="flex gap-2 mt-2">
            <button
              type="submit"
              class="px-3 py-1.5 bg-[#ff5352] text-[#5c0008] text-sm rounded hover:brightness-110 transition-colors"
            >
              Add
            </button>
            <button
              type="button"
              data-action="cancel-add-card"
              @click="showCardForm = false"
              class="px-3 py-1.5 bg-[#3a3939] text-[#e5e2e1]/70 text-sm rounded hover:bg-[#3a3939]/80 transition-colors"
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
          :is-selected="card.id === selectedCardId"
          @click="handleCardClick"
          @dragstart="handleCardDragStart"
          @dragend="handleCardDragEnd"
        />
      </div>

      <!-- Empty State -->
      <div v-if="cards.length === 0 && !showCardForm" class="text-center py-8 text-[#e5e2e1]/40 text-sm">
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
