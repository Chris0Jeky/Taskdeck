<script setup lang="ts">
import { ref } from 'vue'
import type { Card } from '../../types/board'

const props = defineProps<{
  card: Card
  isSelected?: boolean
}>()

const emit = defineEmits<{
  (e: 'click', card: Card): void
  (e: 'dragstart', card: Card): void
  (e: 'dragend'): void
}>()

const isDragging = ref(false)

function isDragHandleTarget(target: EventTarget | null): boolean {
  return target instanceof Element && target.closest('[data-action="drag-card-handle"]') !== null
}

function handleDragStart(event: DragEvent) {
  if (!isDragHandleTarget(event.target)) {
    event.preventDefault()
    return
  }

  // Stop propagation to prevent parent column from being dragged
  event.stopPropagation()

  isDragging.value = true
  if (event.dataTransfer) {
    event.dataTransfer.effectAllowed = 'move'
    event.dataTransfer.setData('text/plain', props.card.id)
  }
  emit('dragstart', props.card)
}

function handleDragEnd() {
  isDragging.value = false
  emit('dragend')
}

function formatDate(dateString: string | null): string {
  if (!dateString) return ''
  const date = new Date(dateString)
  return date.toLocaleDateString()
}

function isOverdue(dateString: string | null): boolean {
  if (!dateString) return false
  return new Date(dateString) < new Date()
}
</script>

<template>
  <div
    draggable="false"
    :data-card-id="card.id"
    :class="[
      'td-board-card group relative cursor-pointer',
      isSelected ? 'td-board-card--selected' : '',
      isDragging ? 'td-board-card--dragging' : ''
    ]"
    tabindex="0"
    :aria-selected="isSelected"
    @click.stop="emit('click', card)"
    @dragstart="handleDragStart"
    @dragend="handleDragEnd"
  >
    <!-- Ember leading-edge indicator -->
    <span class="td-board-card__indicator" aria-hidden="true" />

    <button
      type="button"
      data-action="drag-card-handle"
      draggable="true"
      class="td-card-drag-handle -mx-2 -mt-1 mb-2 flex min-h-10 w-[calc(100%+1rem)] items-center justify-center gap-2 rounded-md px-3 py-2 text-on-surface/60 hover:bg-surface-bright hover:text-on-surface/70 cursor-grab active:cursor-grabbing"
      title="Drag Card"
      aria-label="Drag Card"
      @click.stop
    >
      <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M8 6h.01M8 12h.01M8 18h.01M16 6h.01M16 12h.01M16 18h.01" />
      </svg>
      <span class="td-board-card__drag-label td-board-card__drag-label--hidden">Drag card</span>
    </button>

    <!-- Blocked Badge -->
    <div v-if="card.isBlocked" class="td-board-card__badge-row">
      <span class="td-board-card__badge td-board-card__badge--blocked">
        <svg class="w-3 h-3" fill="currentColor" viewBox="0 0 20 20">
          <path fill-rule="evenodd" d="M13.477 14.89A6 6 0 015.11 6.524l8.367 8.368zm1.414-1.414L6.524 5.11a6 6 0 018.367 8.367zM18 10a8 8 0 11-16 0 8 8 0 0116 0z" clip-rule="evenodd" />
        </svg>
        Blocked
      </span>
    </div>

    <!-- Card Title -->
    <h4 class="td-board-card__title">{{ card.title }}</h4>

    <!-- Card Description (if exists) -->
    <p v-if="card.description" class="td-board-card__description">
      {{ card.description }}
    </p>

    <!-- Labels -->
    <div v-if="card.labels.length > 0" class="td-board-card__labels">
      <span
        v-for="label in card.labels"
        :key="label.id"
        class="td-board-card__label"
        :style="{ backgroundColor: label.colorHex }"
      >
        {{ label.name }}
      </span>
    </div>

    <!-- Due Date -->
    <div v-if="card.dueDate" :class="['td-board-card__due', isOverdue(card.dueDate) ? 'td-board-card__due--overdue' : '']">
      <svg class="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M8 7V3m8 4V3m-9 8h10M5 21h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v12a2 2 0 002 2z" />
      </svg>
      <span>{{ formatDate(card.dueDate) }}</span>
      <span v-if="isOverdue(card.dueDate)" class="font-medium">(Overdue)</span>
    </div>
  </div>
</template>

<style scoped>
/* ── Board Card — token-based visual states ── */
.td-board-card {
  position: relative;
  border-radius: var(--td-radius-lg);
  padding: var(--td-space-4);
  background: var(--td-surface-container-low);
  border: 0.5px solid var(--td-border-ghost);
  box-shadow: var(--td-shadow-sm);
  transition:
    background-color var(--td-transition-fast),
    border-color var(--td-transition-fast),
    box-shadow var(--td-transition-fast),
    transform var(--td-transition-fast),
    opacity var(--td-transition-fast);
}

.td-board-card:hover {
  background: var(--td-surface-container);
  border-color: var(--td-border-default);
  box-shadow: var(--td-shadow-md);
}

.td-board-card:focus-visible {
  outline: none;
  box-shadow: var(--td-focus-ring);
}

/* Selected state */
.td-board-card--selected {
  border-color: var(--td-border-ember);
  box-shadow: var(--td-shadow-md), 0 0 0 2px rgba(255, 83, 82, 0.2);
  background: var(--td-color-ember-dim);
}

.td-board-card--selected:hover {
  box-shadow: var(--td-shadow-lg), 0 0 0 2px rgba(255, 83, 82, 0.25);
}

.td-board-card--selected:focus-visible {
  outline: none;
  box-shadow: var(--td-focus-ring), 0 0 0 4px rgba(255, 83, 82, 0.15);
  border-color: var(--td-border-ember);
  background: var(--td-color-ember-dim);
}

/* Dragging state */
.td-board-card--dragging {
  opacity: 0.5;
  transform: scale(0.95);
}

/* ── Ember leading-edge indicator ── */
.td-board-card__indicator {
  position: absolute;
  left: 0;
  top: var(--td-space-4);
  bottom: var(--td-space-4);
  width: 2px;
  border-radius: 1px;
  background-color: var(--td-color-ember-glow);
  opacity: 0;
  transition: opacity var(--td-transition-fast);
}

.td-board-card:hover .td-board-card__indicator {
  opacity: 1;
}

/* ── Drag handle label ── */
.td-board-card__drag-label {
  font-size: var(--td-font-xs);
  font-family: 'Space Grotesk', system-ui, sans-serif;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.2em;
  transition: opacity var(--td-transition-fast);
}

/* Hide "Drag card" text by default; reveal only on drag-handle hover.
   Use width/overflow collapse (not just opacity) so the invisible text
   does not consume horizontal space in the flex button layout. */
.td-board-card__drag-label--hidden {
  opacity: 0;
  width: 0;
  overflow: hidden;
}

.td-card-drag-handle:hover .td-board-card__drag-label--hidden {
  opacity: 1;
  width: auto;
  overflow: visible;
}

/* ── Badge row ── */
.td-board-card__badge-row {
  margin-bottom: var(--td-space-3);
}

.td-board-card__badge {
  display: inline-flex;
  align-items: center;
  gap: var(--td-space-1);
  padding: 1px var(--td-space-2);
  font-size: var(--td-font-xs);
  font-weight: 600;
  border-radius: 9999px;
}

.td-board-card__badge--blocked {
  background: var(--td-color-error-light);
  color: var(--td-color-error);
}

/* ── Title — highest in hierarchy ── */
.td-board-card__title {
  font-size: var(--td-font-base);
  font-weight: 700;
  font-family: 'Manrope', system-ui, sans-serif;
  color: var(--td-text-primary);
  min-width: 0;
  overflow-wrap: break-word;
  margin-bottom: var(--td-space-3);
  transition: color var(--td-transition-fast);
}

.td-board-card:hover .td-board-card__title {
  color: var(--td-color-primary);
}

/* ── Description — secondary text ── */
.td-board-card__description {
  font-size: var(--td-font-sm);
  color: var(--td-text-muted);
  margin-bottom: var(--td-space-3);
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
  line-height: 1.5;
}

/* ── Labels ── */
.td-board-card__labels {
  display: flex;
  flex-wrap: wrap;
  gap: var(--td-space-1);
  margin-bottom: var(--td-space-3);
}

.td-board-card__label {
  padding: 1px var(--td-space-2);
  font-size: var(--td-font-xs);
  font-family: 'Space Grotesk', system-ui, sans-serif;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.15em;
  border-radius: var(--td-radius-sm);
  color: white;
}

/* ── Due date — tertiary metadata ── */
.td-board-card__due {
  display: flex;
  align-items: center;
  gap: var(--td-space-1);
  font-size: var(--td-font-sm);
  color: var(--td-text-tertiary);
}

.td-board-card__due--overdue {
  color: var(--td-color-error);
}
</style>
