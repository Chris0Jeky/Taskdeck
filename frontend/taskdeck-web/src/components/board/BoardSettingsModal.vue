<script setup lang="ts">
import { ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useBoardStore } from '../../store/boardStore'
import type { Board } from '../../types/board'

const props = defineProps<{
  board: Board
  isOpen: boolean
}>()

const emit = defineEmits<{
  (e: 'close'): void
  (e: 'updated'): void
}>()

const boardStore = useBoardStore()
const router = useRouter()

// Form state
const name = ref('')
const description = ref('')
const isArchived = ref(false)

// Watch for board changes
watch(() => props.board, (newBoard) => {
  if (newBoard) {
    name.value = newBoard.name
    description.value = newBoard.description || ''
    isArchived.value = newBoard.isArchived
  }
}, { immediate: true })

const isFormValid = () => {
  return name.value.trim().length > 0
}

async function handleSave() {
  if (!isFormValid()) return

  try {
    await boardStore.updateBoard(props.board.id, {
      name: name.value !== props.board.name ? name.value : null,
      description: description.value !== props.board.description ? description.value : null,
      isArchived: isArchived.value !== props.board.isArchived ? isArchived.value : null
    })

    emit('updated')
    emit('close')
  } catch (error) {
    console.error('Failed to update board:', error)
  }
}

async function handleDelete() {
  if (!confirm(`Are you sure you want to delete "${props.board.name}"?\n\nThis will permanently delete all columns and cards in this board.`)) return

  try {
    await boardStore.deleteBoard(props.board.id)
    emit('close')
    router.push('/boards')
  } catch (error) {
    console.error('Failed to delete board:', error)
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
          <h2 class="text-2xl font-semibold text-gray-900">Board Settings</h2>
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
            <label for="board-name" class="block text-sm font-medium text-gray-700 mb-1">
              Board Name *
            </label>
            <input
              id="board-name"
              v-model="name"
              type="text"
              required
              class="w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500"
              placeholder="My Board"
            />
          </div>

          <!-- Description -->
          <div>
            <label for="board-description" class="block text-sm font-medium text-gray-700 mb-1">
              Description
            </label>
            <textarea
              id="board-description"
              v-model="description"
              rows="3"
              class="w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500"
              placeholder="What is this board for?"
            ></textarea>
          </div>

          <!-- Archive Status -->
          <div class="border border-gray-200 rounded-md p-4">
            <div class="flex items-center">
              <input
                id="board-archived"
                v-model="isArchived"
                type="checkbox"
                class="w-4 h-4 text-blue-600 border-gray-300 rounded focus:ring-blue-500"
              />
              <label for="board-archived" class="ml-2 text-sm font-medium text-gray-700">
                Archive this board
              </label>
            </div>
            <p class="mt-1 text-xs text-gray-500">
              Archived boards are hidden from the main boards list but can be accessed by including archived boards in search.
            </p>
          </div>

          <!-- Metadata -->
          <div class="pt-4 border-t border-gray-200">
            <div class="text-xs text-gray-500 space-y-1">
              <p>Created: {{ new Date(board.createdAt).toLocaleString() }}</p>
              <p>Last updated: {{ new Date(board.updatedAt).toLocaleString() }}</p>
            </div>
          </div>
        </div>

        <!-- Actions -->
        <div class="mt-6 flex items-center justify-between">
          <button
            @click="handleDelete"
            type="button"
            class="px-4 py-2 text-sm font-medium text-red-600 hover:text-red-700 hover:bg-red-50 border border-red-300 rounded-md transition-colors"
          >
            Delete Board
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
