<script setup lang="ts">
import { ref, computed } from 'vue'
import { useBoardStore } from '../../store/boardStore'
import type { Label } from '../../types/board'

const props = defineProps<{
  isOpen: boolean
  boardId: string
  labels: Label[]
}>()

const emit = defineEmits<{
  (e: 'close'): void
  (e: 'updated'): void
}>()

const boardStore = useBoardStore()

// Form state for new/edit label
const showLabelForm = ref(false)
const editingLabelId = ref<string | null>(null)
const labelName = ref('')
const labelColor = ref('#3B82F6')

// Predefined color palette
const colorPalette = [
  '#EF4444', // Red
  '#F59E0B', // Amber
  '#10B981', // Green
  '#3B82F6', // Blue
  '#6366F1', // Indigo
  '#8B5CF6', // Purple
  '#EC4899', // Pink
  '#64748B', // Slate
  '#0EA5E9', // Sky
  '#14B8A6', // Teal
]

const sortedLabels = computed(() => {
  return [...props.labels].sort((a, b) => a.name.localeCompare(b.name))
})

const isFormValid = () => {
  return labelName.value.trim().length > 0 && /^#[0-9A-F]{6}$/i.test(labelColor.value)
}

function startCreating() {
  editingLabelId.value = null
  labelName.value = ''
  labelColor.value = colorPalette[0] ?? '#3B82F6'
  showLabelForm.value = true
}

function startEditing(label: Label) {
  editingLabelId.value = label.id
  labelName.value = label.name
  labelColor.value = label.colorHex
  showLabelForm.value = true
}

function cancelForm() {
  showLabelForm.value = false
  editingLabelId.value = null
  labelName.value = ''
  labelColor.value = '#3B82F6'
}

async function handleSaveLabel() {
  if (!isFormValid()) return

  try {
    if (editingLabelId.value) {
      // Update existing label
      await boardStore.updateLabel(props.boardId, editingLabelId.value, {
        name: labelName.value,
        colorHex: labelColor.value
      })
    } else {
      // Create new label
      await boardStore.createLabel(props.boardId, {
        name: labelName.value,
        colorHex: labelColor.value
      })
    }

    cancelForm()
    emit('updated')
  } catch (error) {
    console.error('Failed to save label:', error)
  }
}

async function handleDeleteLabel(label: Label) {
  if (!confirm(`Delete label "${label.name}"?\n\nThis will remove the label from all cards.`)) return

  try {
    await boardStore.deleteLabel(props.boardId, label.id)
    emit('updated')
  } catch (error) {
    console.error('Failed to delete label:', error)
  }
}

function handleClose() {
  cancelForm()
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
      <div class="relative bg-white rounded-lg shadow-xl max-w-lg w-full p-6" @click.stop>
        <!-- Header -->
        <div class="flex items-start justify-between mb-4">
          <h2 class="text-2xl font-semibold text-gray-900">Manage Labels</h2>
          <button
            @click="handleClose"
            class="text-gray-400 hover:text-gray-600 transition-colors"
          >
            <svg class="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>

        <!-- Create/Edit Form -->
        <div v-if="showLabelForm" class="mb-4 p-4 border border-gray-200 rounded-lg bg-gray-50">
          <h3 class="text-sm font-medium text-gray-900 mb-3">
            {{ editingLabelId ? 'Edit Label' : 'Create New Label' }}
          </h3>
          <div class="space-y-3">
            <!-- Name -->
            <div>
              <label for="label-name" class="block text-sm font-medium text-gray-700 mb-1">
                Label Name *
              </label>
              <input
                id="label-name"
                v-model="labelName"
                type="text"
                required
                class="w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500"
                placeholder="e.g., Bug, Feature, Priority"
              />
            </div>

            <!-- Color Picker -->
            <div>
              <label class="block text-sm font-medium text-gray-700 mb-2">
                Color *
              </label>
              <div class="flex flex-wrap gap-2 mb-2">
                <button
                  v-for="color in colorPalette"
                  :key="color"
                  @click="labelColor = color"
                  type="button"
                  class="w-8 h-8 rounded-md border-2 transition-all"
                  :class="labelColor === color ? 'border-gray-900 ring-2 ring-offset-2 ring-gray-900' : 'border-gray-300'"
                  :style="{ backgroundColor: color }"
                  :title="color"
                ></button>
              </div>
              <div class="flex items-center gap-2">
                <input
                  v-model="labelColor"
                  type="color"
                  class="w-12 h-8 border border-gray-300 rounded cursor-pointer"
                />
                <input
                  v-model="labelColor"
                  type="text"
                  pattern="^#[0-9A-Fa-f]{6}$"
                  class="flex-1 px-3 py-1.5 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
                  placeholder="#3B82F6"
                />
              </div>
            </div>

            <!-- Preview -->
            <div>
              <label class="block text-sm font-medium text-gray-700 mb-1">
                Preview
              </label>
              <span
                class="inline-block px-3 py-1.5 rounded-md text-sm font-medium text-white"
                :style="{ backgroundColor: labelColor }"
              >
                {{ labelName || 'Label Name' }}
              </span>
            </div>

            <!-- Actions -->
            <div class="flex gap-2 pt-2">
              <button
                @click="handleSaveLabel"
                :disabled="!isFormValid()"
                type="button"
                class="px-4 py-2 text-sm font-medium text-white bg-blue-600 hover:bg-blue-700 disabled:bg-gray-400 disabled:cursor-not-allowed rounded-md transition-colors"
              >
                {{ editingLabelId ? 'Update' : 'Create' }}
              </button>
              <button
                @click="cancelForm"
                type="button"
                class="px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-100 border border-gray-300 rounded-md transition-colors"
              >
                Cancel
              </button>
            </div>
          </div>
        </div>

        <!-- Create Button -->
        <button
          v-if="!showLabelForm"
          @click="startCreating"
          class="w-full mb-4 px-4 py-2 text-sm font-medium text-blue-600 hover:bg-blue-50 border border-blue-300 rounded-md transition-colors flex items-center justify-center gap-2"
        >
          <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 4v16m8-8H4" />
          </svg>
          Create New Label
        </button>

        <!-- Labels List -->
        <div class="space-y-2 max-h-96 overflow-y-auto">
          <div v-if="sortedLabels.length === 0" class="text-center py-8 text-gray-400">
            <p class="text-sm">No labels yet</p>
            <p class="text-xs mt-1">Create your first label to get started</p>
          </div>

          <div
            v-for="label in sortedLabels"
            :key="label.id"
            class="flex items-center justify-between p-3 border border-gray-200 rounded-lg hover:bg-gray-50 transition-colors"
          >
            <div class="flex items-center gap-3 flex-1 min-w-0">
              <span
                class="inline-block px-3 py-1.5 rounded-md text-sm font-medium text-white flex-shrink-0"
                :style="{ backgroundColor: label.colorHex }"
              >
                {{ label.name }}
              </span>
              <span class="text-xs text-gray-500 truncate">{{ label.colorHex }}</span>
            </div>
            <div class="flex gap-1 flex-shrink-0">
              <button
                @click="startEditing(label)"
                class="p-1.5 text-gray-600 hover:text-blue-600 hover:bg-blue-50 rounded transition-colors"
                title="Edit"
              >
                <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" />
                </svg>
              </button>
              <button
                @click="handleDeleteLabel(label)"
                class="p-1.5 text-gray-600 hover:text-red-600 hover:bg-red-50 rounded transition-colors"
                title="Delete"
              >
                <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                </svg>
              </button>
            </div>
          </div>
        </div>

        <!-- Footer -->
        <div class="mt-6 pt-4 border-t border-gray-200">
          <button
            @click="handleClose"
            type="button"
            class="w-full px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 border border-gray-300 rounded-md transition-colors"
          >
            Done
          </button>
        </div>
      </div>
    </div>
  </div>
</template>
