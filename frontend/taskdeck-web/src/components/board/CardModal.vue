<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useBoardStore } from '../../store/boardStore'
import type { Card, Label } from '../../types/board'

const props = defineProps<{
  card: Card
  isOpen: boolean
  labels: Label[]
}>()

const emit = defineEmits<{
  (e: 'close'): void
  (e: 'updated'): void
}>()

const boardStore = useBoardStore()

// Form state
const title = ref('')
const description = ref('')
const dueDate = ref('')
const isBlocked = ref(false)
const blockReason = ref('')
const selectedLabelIds = ref<string[]>([])

// Watch for card changes
watch(() => props.card, (newCard) => {
  if (newCard) {
    title.value = newCard.title
    description.value = newCard.description || ''
    dueDate.value = newCard.dueDate
      ? new Date(newCard.dueDate).toISOString().split('T')[0] ?? ''
      : ''
    isBlocked.value = newCard.isBlocked
    blockReason.value = newCard.blockReason || ''
    selectedLabelIds.value = newCard.labels.map(l => l.id)
  }
}, { immediate: true })

const formattedDueDate = computed(() => {
  if (!props.card.dueDate) return 'No due date'
  const date = new Date(props.card.dueDate)
  return date.toLocaleDateString('en-US', { year: 'numeric', month: 'long', day: 'numeric' })
})

const isOverdue = computed(() => {
  if (!props.card.dueDate) return false
  return new Date(props.card.dueDate) < new Date()
})

const isFormValid = computed(() => {
  if (title.value.trim().length === 0) return false
  if (isBlocked.value && blockReason.value.trim().length === 0) return false
  return true
})

async function handleSave() {
  if (!isFormValid.value) return

  try {
    await boardStore.updateCard(props.card.boardId, props.card.id, {
      title: title.value !== props.card.title ? title.value : null,
      description: description.value !== props.card.description ? description.value : null,
      dueDate: dueDate.value ? new Date(dueDate.value).toISOString() : null,
      isBlocked: isBlocked.value !== props.card.isBlocked ? isBlocked.value : null,
      blockReason: isBlocked.value ? blockReason.value : null,
      labelIds: selectedLabelIds.value
    })

    emit('updated')
    emit('close')
  } catch (error) {
    console.error('Failed to update card:', error)
  }
}

async function handleDelete() {
  if (!confirm(`Are you sure you want to delete "${props.card.title}"?`)) return

  try {
    await boardStore.deleteCard(props.card.boardId, props.card.id)
    emit('updated')
    emit('close')
  } catch (error) {
    console.error('Failed to delete card:', error)
  }
}

function handleClose() {
  emit('close')
}

function toggleLabel(labelId: string) {
  const index = selectedLabelIds.value.indexOf(labelId)
  if (index > -1) {
    selectedLabelIds.value.splice(index, 1)
  } else {
    selectedLabelIds.value.push(labelId)
  }
}

function clearDueDate() {
  dueDate.value = ''
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
      <div class="relative bg-white rounded-lg shadow-xl max-w-2xl w-full p-6" @click.stop>
        <!-- Header -->
        <div class="flex items-start justify-between mb-4">
          <h2 class="text-2xl font-semibold text-gray-900">Edit Card</h2>
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
          <!-- Title -->
          <div>
            <label for="card-title" class="block text-sm font-medium text-gray-700 mb-1">
              Title *
            </label>
            <input
              id="card-title"
              v-model="title"
              type="text"
              required
              class="w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500"
              placeholder="Card title"
            />
          </div>

          <!-- Description -->
          <div>
            <label for="card-description" class="block text-sm font-medium text-gray-700 mb-1">
              Description
            </label>
            <textarea
              id="card-description"
              v-model="description"
              rows="4"
              class="w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500"
              placeholder="Add a more detailed description..."
            ></textarea>
          </div>

          <!-- Due Date -->
          <div>
            <label for="card-due-date" class="block text-sm font-medium text-gray-700 mb-1">
              Due Date
            </label>
            <div class="flex gap-2">
              <input
                id="card-due-date"
                v-model="dueDate"
                type="date"
                class="flex-1 px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
              <button
                v-if="dueDate"
                @click="clearDueDate"
                type="button"
                class="px-3 py-2 text-sm text-gray-600 hover:text-gray-800 border border-gray-300 rounded-md hover:bg-gray-50"
              >
                Clear
              </button>
            </div>
            <p v-if="card.dueDate" class="mt-1 text-xs" :class="isOverdue ? 'text-red-600' : 'text-gray-500'">
              Current: {{ formattedDueDate }}
              <span v-if="isOverdue" class="font-medium">(Overdue)</span>
            </p>
          </div>

          <!-- Blocked Status -->
          <div class="border border-gray-200 rounded-md p-4">
            <div class="flex items-center mb-2">
              <input
                id="card-blocked"
                v-model="isBlocked"
                type="checkbox"
                class="w-4 h-4 text-blue-600 border-gray-300 rounded focus:ring-blue-500"
              />
              <label for="card-blocked" class="ml-2 text-sm font-medium text-gray-700">
                Mark as blocked
              </label>
            </div>
            <div v-if="isBlocked">
              <label for="block-reason" class="block text-sm font-medium text-gray-700 mb-1">
                Block Reason *
              </label>
              <textarea
                id="block-reason"
                v-model="blockReason"
                rows="2"
                required
                class="w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500"
                placeholder="Why is this card blocked?"
              ></textarea>
            </div>
          </div>

          <!-- Labels -->
          <div>
            <label class="block text-sm font-medium text-gray-700 mb-2">
              Labels
            </label>
            <div v-if="labels.length > 0" class="flex flex-wrap gap-2">
              <button
                v-for="label in labels"
                :key="label.id"
                @click="toggleLabel(label.id)"
                type="button"
                class="px-3 py-1.5 rounded-md text-sm font-medium transition-all"
                :class="selectedLabelIds.includes(label.id)
                  ? 'text-white ring-2 ring-offset-2 ring-blue-500'
                  : 'text-gray-700 bg-gray-100 hover:bg-gray-200'"
                :style="selectedLabelIds.includes(label.id) ? { backgroundColor: label.colorHex } : {}"
              >
                {{ label.name }}
              </button>
            </div>
            <p v-else class="text-sm text-gray-500 italic">No labels available</p>
          </div>

          <!-- Metadata -->
          <div class="pt-4 border-t border-gray-200">
            <div class="text-xs text-gray-500 space-y-1">
              <p>Created: {{ new Date(card.createdAt).toLocaleString() }}</p>
              <p>Last updated: {{ new Date(card.updatedAt).toLocaleString() }}</p>
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
            Delete Card
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
              :disabled="!isFormValid"
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
