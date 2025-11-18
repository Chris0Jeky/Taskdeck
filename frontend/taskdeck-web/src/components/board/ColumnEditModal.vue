<script setup lang="ts">
import { ref, watch } from 'vue'
import { useBoardStore } from '../../store/boardStore'
import type { Column } from '../../types/board'

const props = defineProps<{
  column: Column
  isOpen: boolean
  boardId: string
}>()

const emit = defineEmits<{
  (e: 'close'): void
  (e: 'updated'): void
}>()

const boardStore = useBoardStore()

// Form state
const name = ref('')
const wipLimit = ref<number | null>(null)
const hasWipLimit = ref(false)

// Watch for column changes
watch(() => props.column, (newColumn) => {
  if (newColumn) {
    name.value = newColumn.name
    wipLimit.value = newColumn.wipLimit
    hasWipLimit.value = newColumn.wipLimit !== null
  }
}, { immediate: true })

const isFormValid = () => {
  if (name.value.trim().length === 0) return false
  if (hasWipLimit.value && (wipLimit.value === null || wipLimit.value <= 0)) return false
  return true
}

async function handleSave() {
  if (!isFormValid()) return

  try {
    await boardStore.updateColumn(props.boardId, props.column.id, {
      name: name.value !== props.column.name ? name.value : null,
      wipLimit: hasWipLimit.value ? wipLimit.value : null,
      position: null
    })

    emit('updated')
    emit('close')
  } catch (error) {
    console.error('Failed to update column:', error)
  }
}

async function handleDelete() {
  if (props.column.cardCount > 0) {
    alert(`Cannot delete column "${props.column.name}" because it contains ${props.column.cardCount} card(s).\n\nPlease move or delete all cards first.`)
    return
  }

  if (!confirm(`Are you sure you want to delete "${props.column.name}"?`)) return

  try {
    await boardStore.deleteColumn(props.boardId, props.column.id)
    emit('updated')
    emit('close')
  } catch (error) {
    console.error('Failed to delete column:', error)
  }
}

function handleClose() {
  emit('close')
}
</script>

<template>
  <div
    v-if="isOpen"
    class="fixed inset-0 z-50 overflow-y-auto"
    @click.self="handleClose"
  >
    <!-- Backdrop -->
    <div class="fixed inset-0 bg-black bg-opacity-50 transition-opacity"></div>

    <!-- Modal -->
    <div class="flex min-h-full items-center justify-center p-4">
      <div class="relative bg-white rounded-lg shadow-xl max-w-md w-full p-6" @click.stop>
        <!-- Header -->
        <div class="flex items-start justify-between mb-4">
          <h2 class="text-2xl font-semibold text-gray-900">Edit Column</h2>
          <button
            @click="handleClose"
            class="text-gray-400 hover:text-gray-600 transition-colors"
          >
            <svg class="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>

        <!-- Form -->
        <div class="space-y-4">
          <!-- Name -->
          <div>
            <label for="column-name" class="block text-sm font-medium text-gray-700 mb-1">
              Column Name *
            </label>
            <input
              id="column-name"
              v-model="name"
              type="text"
              required
              class="w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500"
              placeholder="To Do"
            />
          </div>

          <!-- WIP Limit -->
          <div class="border border-gray-200 rounded-md p-4">
            <div class="flex items-center mb-2">
              <input
                id="column-has-wip-limit"
                v-model="hasWipLimit"
                type="checkbox"
                class="w-4 h-4 text-blue-600 border-gray-300 rounded focus:ring-blue-500"
              />
              <label for="column-has-wip-limit" class="ml-2 text-sm font-medium text-gray-700">
                Set WIP limit
              </label>
            </div>
            <div v-if="hasWipLimit">
              <label for="wip-limit" class="block text-sm font-medium text-gray-700 mb-1">
                Maximum cards in column *
              </label>
              <input
                id="wip-limit"
                v-model.number="wipLimit"
                type="number"
                min="1"
                required
                class="w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500"
                placeholder="e.g., 3"
              />
              <p class="mt-1 text-xs text-gray-500">
                Limits work-in-progress to help maintain focus. You'll be warned when adding more cards than this limit.
              </p>
            </div>
          </div>

          <!-- Column Info -->
          <div class="bg-gray-50 rounded-md p-3">
            <div class="text-sm text-gray-700 space-y-1">
              <p><span class="font-medium">Position:</span> {{ column.position + 1 }}</p>
              <p><span class="font-medium">Cards:</span> {{ column.cardCount }}</p>
              <p class="text-xs text-gray-500 mt-2">Created: {{ new Date(column.createdAt).toLocaleString() }}</p>
            </div>
          </div>
        </div>

        <!-- Actions -->
        <div class="mt-6 flex items-center justify-between">
          <button
            @click="handleDelete"
            type="button"
            :disabled="column.cardCount > 0"
            :class="column.cardCount > 0 ? 'opacity-50 cursor-not-allowed' : ''"
            class="px-4 py-2 text-sm font-medium text-red-600 hover:text-red-700 hover:bg-red-50 border border-red-300 rounded-md transition-colors"
          >
            Delete Column
          </button>
          <div class="flex gap-2">
            <button
              @click="handleClose"
              type="button"
              class="px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 border border-gray-300 rounded-md transition-colors"
            >
              Cancel
            </button>
            <button
              @click="handleSave"
              :disabled="!isFormValid()"
              type="button"
              class="px-4 py-2 text-sm font-medium text-white bg-blue-600 hover:bg-blue-700 disabled:bg-gray-400 disabled:cursor-not-allowed rounded-md transition-colors"
            >
              Save Changes
            </button>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
