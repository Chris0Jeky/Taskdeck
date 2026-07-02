<script setup lang="ts">
import type { Card } from '../../../types/board'

defineProps<{
  card: Card
  formattedDueDate: string
  isOverdue: boolean
}>()

const title = defineModel<string>('title', { required: true })
const description = defineModel<string>('description', { required: true })
const dueDate = defineModel<string>('dueDate', { required: true })
const isBlocked = defineModel<boolean>('isBlocked', { required: true })
const blockReason = defineModel<string>('blockReason', { required: true })

defineEmits<{
  (e: 'clear-due-date'): void
}>()
</script>

<template>
  <!-- Title -->
  <div>
    <label for="card-title" class="block text-sm font-medium text-on-surface-variant mb-1">
      Title *
    </label>
    <input
      id="card-title"
      v-model="title"
      type="text"
      required
      class="w-full px-3 py-2 bg-surface-container-high border border-outline-variant/40 rounded-md text-on-surface placeholder-on-surface-variant/50 focus:outline-none focus:ring-2 focus:ring-primary"
      placeholder="Card title"
    />
  </div>

  <!-- Description -->
  <div>
    <label for="card-description" class="block text-sm font-medium text-on-surface-variant mb-1">
      Description
    </label>
    <textarea
      id="card-description"
      v-model="description"
      rows="4"
      class="w-full px-3 py-2 bg-surface-container-high border border-outline-variant/40 rounded-md text-on-surface placeholder-on-surface-variant/50 focus:outline-none focus:ring-2 focus:ring-primary"
      placeholder="Add a more detailed description..."
    ></textarea>
  </div>

  <!-- Due Date -->
  <div>
    <label for="card-due-date" class="block text-sm font-medium text-on-surface-variant mb-1">
      Due Date
    </label>
    <div class="flex gap-2">
      <input
        id="card-due-date"
        v-model="dueDate"
        type="date"
        class="flex-1 px-3 py-2 bg-surface-container-high border border-outline-variant/40 rounded-md text-on-surface focus:outline-none focus:ring-2 focus:ring-primary"
      />
      <button
        v-if="dueDate"
        @click="$emit('clear-due-date')"
        type="button"
        class="px-3 py-2 text-sm text-on-surface-variant hover:text-on-surface border border-outline-variant/40 rounded-md hover:bg-surface-container-high transition-colors"
      >
        Clear
      </button>
    </div>
    <p v-if="card.dueDate" class="mt-1 text-xs" :class="isOverdue ? 'text-error' : 'text-on-surface-variant'">
      Current: {{ formattedDueDate }}
      <span v-if="isOverdue" class="font-medium">(Overdue)</span>
    </p>
  </div>

  <!-- Blocked Status -->
  <div class="border border-outline-variant/30 rounded-md p-4">
    <div class="flex items-center mb-2">
      <input
        id="card-is-blocked"
        v-model="isBlocked"
        type="checkbox"
        class="w-4 h-4 text-primary border-outline-variant rounded focus:ring-primary"
      />
      <label for="card-is-blocked" class="ml-2 text-sm font-medium text-on-surface-variant">
        Mark as blocked
      </label>
    </div>
    <div v-if="isBlocked">
      <label for="card-block-reason" class="block text-sm font-medium text-on-surface-variant mb-1">
        Block Reason *
      </label>
      <textarea
        id="card-block-reason"
        v-model="blockReason"
        rows="2"
        required
        class="w-full px-3 py-2 bg-surface-container-high border border-outline-variant/40 rounded-md text-on-surface placeholder-on-surface-variant/50 focus:outline-none focus:ring-2 focus:ring-primary"
        placeholder="Why is this card blocked?"
      ></textarea>
    </div>
  </div>
</template>
