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
      'td-column-lane',
      isDragOver ? 'td-column-lane--drag-over' : ''
    ]"
    @dragover="handleDragOver"
    @dragleave="handleDragLeave"
    @drop="handleDrop"
  >
    <!-- Column Header -->
    <div class="td-column-lane__header">
      <div class="td-column-lane__header-row">
        <h3 class="td-column-lane__title"><span class="td-column-lane__title-dot"></span>{{ column.name }}</h3>
        <div class="td-column-lane__actions">
          <button
            type="button"
            data-action="drag-column-handle"
            draggable="true"
            class="td-column-lane__icon-btn cursor-grab active:cursor-grabbing"
            title="Drag Column"
            aria-label="Drag Column"
            @click.stop
          >
            <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M8 6h.01M8 12h.01M8 18h.01M16 6h.01M16 12h.01M16 18h.01" />
            </svg>
          </button>
          <span
            class="td-column-lane__count"
            :class="isWipLimitExceeded() ? 'td-column-lane__count--exceeded' : ''"
          >
            {{ cards.length }}{{ column.wipLimit ? `/${column.wipLimit}` : '' }}
          </span>
          <button
            @click="showColumnEdit = true"
            class="td-column-lane__icon-btn"
            title="Edit Column"
          >
            <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.065 2.572c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.572 1.065c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.065-2.572c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z" />
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
            </svg>
          </button>
        </div>
      </div>

      <div v-if="isWipLimitExceeded()" class="td-column-lane__wip-warning">
        WIP limit exceeded
      </div>

      <button
        data-action="toggle-add-card"
        @click="openCardForm"
        class="td-column-lane__add-card-btn"
      >
        <span>+</span>
        <span>Add Card</span>
      </button>

      <!-- Create Card Form -->
      <div
        v-if="showCardForm"
        data-action="add-card-form"
        class="td-column-lane__card-form"
      >
        <form @submit.prevent="createCard">
          <textarea
            data-action="add-card-input"
            v-model="newCardTitle"
            placeholder="Enter card title..."
            class="td-column-lane__card-input"
            rows="3"
            autofocus
          ></textarea>
          <div class="td-column-lane__card-form-actions">
            <button
              type="submit"
              class="td-column-lane__form-btn td-column-lane__form-btn--primary"
            >
              Add
            </button>
            <button
              type="button"
              data-action="cancel-add-card"
              @click="showCardForm = false"
              class="td-column-lane__form-btn td-column-lane__form-btn--secondary"
            >
              Cancel
            </button>
          </div>
        </form>
      </div>
    </div>

    <!-- Cards List -->
    <div class="td-column-lane__cards">
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
      <div v-if="cards.length === 0 && !showCardForm" class="td-column-lane__empty">
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

<style scoped>
/* ── Column Lane — token-based layout ── */
.td-column-lane {
  flex-shrink: 0;
  width: 20rem;
  border-radius: var(--td-radius-lg);
  padding: var(--td-space-5);
  background: var(--td-surface-container-low);
  border: 0.5px solid var(--td-border-ghost);
  transition:
    background-color var(--td-transition-fast),
    border-color var(--td-transition-fast),
    box-shadow var(--td-transition-fast);
}

.td-column-lane--drag-over {
  background: var(--td-color-ember-dim);
  border-color: var(--td-border-ember);
  box-shadow: var(--td-shadow-sm);
}

/* ── Column header ── */
.td-column-lane__header {
  margin-bottom: var(--td-space-5);
}

.td-column-lane__header-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: var(--td-space-3);
}

.td-column-lane__title {
  display: flex;
  align-items: center;
  gap: var(--td-space-2);
  flex: 1;
  font-family: 'Space Grotesk', system-ui, sans-serif;
  font-size: var(--td-font-xs);
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.2em;
  color: var(--td-text-primary);
}

.td-column-lane__title-dot {
  width: 3px;
  height: 1rem;
  border-radius: 9999px;
  background: var(--td-color-ember-glow);
  flex-shrink: 0;
}

.td-column-lane__actions {
  display: flex;
  align-items: center;
  gap: var(--td-space-2);
}

.td-column-lane__icon-btn {
  padding: var(--td-space-1);
  color: var(--td-text-tertiary);
  border-radius: var(--td-radius-md);
  transition:
    color var(--td-transition-fast),
    background-color var(--td-transition-fast);
}

.td-column-lane__icon-btn:hover {
  color: var(--td-text-secondary);
  background: var(--td-surface-bright);
}

.td-column-lane__icon-btn:focus-visible {
  outline: none;
  box-shadow: var(--td-focus-ring);
}

/* ── Card count badge ── */
.td-column-lane__count {
  font-size: var(--td-font-sm);
  padding: var(--td-space-1) var(--td-space-2);
  border-radius: var(--td-radius-md);
  background: var(--td-surface-bright);
  color: var(--td-text-muted);
}

.td-column-lane__count--exceeded {
  background: var(--td-color-error-light);
  color: var(--td-color-error);
}

/* ── WIP warning ── */
.td-column-lane__wip-warning {
  font-size: var(--td-font-sm);
  color: var(--td-color-error);
  margin-bottom: var(--td-space-3);
}

/* ── Add card button ── */
.td-column-lane__add-card-btn {
  width: 100%;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: var(--td-space-1);
  padding: var(--td-space-2) var(--td-space-4);
  font-size: var(--td-font-sm);
  color: var(--td-text-tertiary);
  border: 1px dashed var(--td-border-default);
  border-radius: var(--td-radius-md);
  transition:
    color var(--td-transition-fast),
    border-color var(--td-transition-fast);
}

.td-column-lane__add-card-btn:hover {
  color: var(--td-color-primary);
  border-color: var(--td-color-primary);
}

.td-column-lane__add-card-btn:focus-visible {
  outline: none;
  box-shadow: var(--td-focus-ring);
}

/* ── Card creation form ── */
.td-column-lane__card-form {
  margin-top: var(--td-space-4);
  background: var(--td-surface-container);
  border-radius: var(--td-radius-lg);
  padding: var(--td-space-4);
  box-shadow: var(--td-shadow-sm);
}

.td-column-lane__card-input {
  width: 100%;
  padding: var(--td-space-2) var(--td-space-4);
  background: var(--td-surface-container-low);
  border: 1px solid var(--td-border-default);
  color: var(--td-text-primary);
  border-radius: var(--td-radius-md);
  resize: none;
  font-size: var(--td-font-base);
}

.td-column-lane__card-input::placeholder {
  color: var(--td-text-tertiary);
}

.td-column-lane__card-input:focus {
  outline: none;
  box-shadow: var(--td-focus-ring);
}

.td-column-lane__card-form-actions {
  display: flex;
  gap: var(--td-space-2);
  margin-top: var(--td-space-3);
}

.td-column-lane__form-btn {
  padding: var(--td-space-1) var(--td-space-4);
  font-size: var(--td-font-sm);
  font-weight: 500;
  border-radius: var(--td-radius-md);
  transition:
    background-color var(--td-transition-fast),
    filter var(--td-transition-fast);
}

.td-column-lane__form-btn--primary {
  background: var(--td-color-primary);
  color: var(--td-text-inverse);
}

.td-column-lane__form-btn--primary:hover {
  background: var(--td-color-primary-hover);
}

.td-column-lane__form-btn--primary:focus-visible {
  outline: none;
  box-shadow: var(--td-focus-ring);
}

.td-column-lane__form-btn--secondary {
  background: var(--td-surface-bright);
  color: var(--td-text-muted);
}

.td-column-lane__form-btn--secondary:hover {
  background: var(--td-surface-container-highest);
}

.td-column-lane__form-btn--secondary:focus-visible {
  outline: none;
  box-shadow: var(--td-focus-ring);
}

/* ── Cards list ── */
.td-column-lane__cards {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-3);
  overflow-y: auto;
  max-height: calc(100vh - 280px);
  min-height: 100px;
}

/* ── Empty state ── */
.td-column-lane__empty {
  text-align: center;
  padding: var(--td-space-8) 0;
  font-size: var(--td-font-sm);
  color: var(--td-text-tertiary);
}
</style>
