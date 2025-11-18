<script setup lang="ts">
import type { Card } from '../../types/board'

defineProps<{
  card: Card
}>()

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
  <div class="bg-white rounded-lg p-3 shadow-sm hover:shadow-md transition-shadow cursor-pointer border border-gray-200">
    <!-- Blocked Badge -->
    <div v-if="card.isBlocked" class="mb-2">
      <span class="inline-flex items-center gap-1 px-2 py-0.5 bg-red-100 text-red-700 text-xs rounded">
        <svg class="w-3 h-3" fill="currentColor" viewBox="0 0 20 20">
          <path fill-rule="evenodd" d="M13.477 14.89A6 6 0 015.11 6.524l8.367 8.368zm1.414-1.414L6.524 5.11a6 6 0 018.367 8.367zM18 10a8 8 0 11-16 0 8 8 0 0116 0z" clip-rule="evenodd" />
        </svg>
        Blocked
      </span>
    </div>

    <!-- Card Title -->
    <h4 class="text-sm font-medium text-gray-900 mb-2">{{ card.title }}</h4>

    <!-- Card Description (if exists) -->
    <p v-if="card.description" class="text-xs text-gray-600 mb-2 line-clamp-2">
      {{ card.description }}
    </p>

    <!-- Labels -->
    <div v-if="card.labels.length > 0" class="flex flex-wrap gap-1 mb-2">
      <span
        v-for="label in card.labels"
        :key="label.id"
        class="px-2 py-0.5 text-xs rounded text-white font-medium"
        :style="{ backgroundColor: label.colorHex }"
      >
        {{ label.name }}
      </span>
    </div>

    <!-- Due Date -->
    <div v-if="card.dueDate" class="flex items-center gap-1 text-xs" :class="isOverdue(card.dueDate) ? 'text-red-600' : 'text-gray-500'">
      <svg class="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M8 7V3m8 4V3m-9 8h10M5 21h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v12a2 2 0 002 2z" />
      </svg>
      <span>{{ formatDate(card.dueDate) }}</span>
      <span v-if="isOverdue(card.dueDate)" class="font-medium">(Overdue)</span>
    </div>
  </div>
</template>
