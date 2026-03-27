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
      'group rounded-lg p-3 shadow-[0_2px_8px_rgba(0,0,0,0.3)] hover:shadow-[0_4px_12px_rgba(0,0,0,0.4)] transition-all cursor-pointer border-[0.5px] relative',
      isSelected ? 'border-primary-container ring-4 ring-primary-container/30 shadow-xl bg-primary-container/10 scale-105' : 'bg-surface-container-low border-outline-variant/15',
      isDragging ? 'opacity-50 scale-95' : ''
    ]"
    @click.stop="emit('click', card)"
    @dragstart="handleDragStart"
    @dragend="handleDragEnd"
  >
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
      <span class="text-[11px] font-semibold uppercase tracking-[0.2em] font-label">Drag card</span>
    </button>

    <!-- Blocked Badge -->
    <div v-if="card.isBlocked" class="mb-2">
      <span class="inline-flex items-center gap-1 px-2 py-0.5 bg-ember/10 text-ember text-xs rounded">
        <svg class="w-3 h-3" fill="currentColor" viewBox="0 0 20 20">
          <path fill-rule="evenodd" d="M13.477 14.89A6 6 0 015.11 6.524l8.367 8.368zm1.414-1.414L6.524 5.11a6 6 0 018.367 8.367zM18 10a8 8 0 11-16 0 8 8 0 0116 0z" clip-rule="evenodd" />
        </svg>
        Blocked
      </span>
    </div>

    <!-- Card Title -->
    <h4 class="text-sm font-bold font-body text-on-surface group-hover:text-primary min-w-0 break-words mb-2 transition-colors">{{ card.title }}</h4>

    <!-- Card Description (if exists) -->
    <p v-if="card.description" class="text-xs text-on-surface/60 mb-2 line-clamp-2">
      {{ card.description }}
    </p>

    <!-- Labels -->
    <div v-if="card.labels.length > 0" class="flex flex-wrap gap-1 mb-2">
      <span
        v-for="label in card.labels"
        :key="label.id"
        class="px-2 py-0.5 text-[9px] uppercase font-label tracking-[0.2em] rounded text-white font-medium"
        :style="{ backgroundColor: label.colorHex }"
      >
        {{ label.name }}
      </span>
    </div>

    <!-- Due Date -->
    <div v-if="card.dueDate" class="flex items-center gap-1 text-xs" :class="isOverdue(card.dueDate) ? 'text-ember' : 'text-on-surface/60'">
      <svg class="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M8 7V3m8 4V3m-9 8h10M5 21h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v12a2 2 0 002 2z" />
      </svg>
      <span>{{ formatDate(card.dueDate) }}</span>
      <span v-if="isOverdue(card.dueDate)" class="font-medium">(Overdue)</span>
    </div>
  </div>
</template>

<style scoped>
/* 2px left ember indicator on hover */
div[data-card-id]::before {
  content: '';
  position: absolute;
  left: 0;
  top: 8px;
  bottom: 8px;
  width: 2px;
  border-radius: 1px;
  background-color: var(--td-color-ember-glow);
  opacity: 0;
  transition: opacity 0.2s ease;
}

div[data-card-id]:hover::before {
  opacity: 1;
}
</style>
