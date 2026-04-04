<script setup lang="ts">
import { ref, computed } from 'vue'
import { useBoardStore } from '../../store/boardStore'
import { useEscapeToClose } from '../../composables/useEscapeToClose'
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

useEscapeToClose(() => props.isOpen, handleClose)
</script>

<template>
  <!-- eslint-disable-next-line vuejs-accessibility/no-static-element-interactions -- modal backdrop with dialog role and escape key handler; click-to-close is standard modal UX -->
  <div
    v-if="isOpen"
    class="fixed inset-0 z-50 overflow-y-auto"
    role="dialog"
    aria-label="Manage Labels"
    aria-modal="true"
    @click.self="handleClose"
    @keydown.escape="handleClose"
  >
    <!-- Backdrop -->
    <div class="fixed inset-0 bg-black bg-opacity-50 transition-opacity"></div>

    <!-- Modal -->
    <div class="flex min-h-full items-center justify-center p-4">
      <div class="relative bg-surface-container rounded-lg shadow-xl max-w-lg w-full p-6 border border-outline-variant/30" @click.stop>
        <!-- Header -->
        <div class="flex items-start justify-between mb-4">
          <h2 class="text-2xl font-semibold text-on-surface">Manage Labels</h2>
          <button
            @click="handleClose"
            class="text-on-surface-variant hover:text-on-surface transition-colors"
          >
            <svg class="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>

        <!-- Create/Edit Form -->
        <div v-if="showLabelForm" class="mb-4 p-4 border border-outline-variant/30 rounded-lg bg-surface-container-high">
          <h3 class="text-sm font-medium text-on-surface mb-3">
            {{ editingLabelId ? 'Edit Label' : 'Create New Label' }}
          </h3>
          <div class="space-y-3">
            <!-- Name -->
            <div>
              <label for="label-name" class="block text-sm font-medium text-on-surface-variant mb-1">
                Label Name *
              </label>
              <input
                id="label-name"
                v-model="labelName"
                type="text"
                required
                class="w-full px-3 py-2 bg-surface-container-high border border-outline-variant/40 rounded-md text-on-surface placeholder-on-surface-variant/50 focus:outline-none focus:ring-2 focus:ring-primary/50"
                placeholder="e.g., Bug, Feature, Priority"
              />
            </div>

            <!-- Color Picker -->
            <div>
              <p class="block text-sm font-medium text-on-surface-variant mb-2">
                Color *
              </p>
              <div class="flex flex-wrap gap-2 mb-2">
                <button
                  v-for="color in colorPalette"
                  :key="color"
                  @click="labelColor = color"
                  type="button"
                  class="w-8 h-8 rounded-md border-2 transition-all"
                  :class="labelColor === color ? 'border-on-surface ring-2 ring-offset-2 ring-offset-surface-container-high ring-on-surface' : 'border-outline-variant/40'"
                  :style="{ backgroundColor: color }"
                  :title="color"
                ></button>
              </div>
              <div class="flex items-center gap-2">
                <label for="label-color-picker" class="sr-only">Color picker</label>
                <input
                  id="label-color-picker"
                  v-model="labelColor"
                  type="color"
                  class="w-12 h-8 border border-outline-variant/40 rounded cursor-pointer bg-surface-container-high"
                />
                <label for="label-color-hex" class="sr-only">Hex color value</label>
                <input
                  id="label-color-hex"
                  v-model="labelColor"
                  type="text"
                  pattern="^#[0-9A-Fa-f]{6}$"
                  class="flex-1 px-3 py-1.5 bg-surface-container-high border border-outline-variant/40 rounded-md text-sm text-on-surface placeholder-on-surface-variant/50 focus:outline-none focus:ring-2 focus:ring-primary/50"
                  placeholder="#3B82F6"
                />
              </div>
            </div>

            <!-- Preview -->
            <div>
              <p class="block text-sm font-medium text-on-surface-variant mb-1">
                Preview
              </p>
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
                class="px-4 py-2 text-sm font-medium text-on-primary-container bg-primary-container hover:brightness-110 disabled:opacity-40 disabled:cursor-not-allowed rounded-md transition-all"
              >
                {{ editingLabelId ? 'Update' : 'Create' }}
              </button>
              <button
                @click="cancelForm"
                type="button"
                class="px-4 py-2 text-sm font-medium text-on-surface-variant hover:bg-surface-container-high border border-outline-variant/40 rounded-md transition-colors"
              >
                Cancel
              </button>
            </div>
          </div>
        </div>

        <!-- Labels List -->
        <div class="space-y-2 max-h-96 overflow-y-auto">
          <div v-if="sortedLabels.length === 0" class="text-center py-8 text-on-surface-variant/60">
            <p class="text-sm">No labels yet</p>
            <p class="text-xs mt-1">Create your first label to get started</p>
          </div>

          <div
            v-for="label in sortedLabels"
            :key="label.id"
            class="flex items-center justify-between p-3 border border-outline-variant/30 rounded-lg hover:bg-surface-container-high transition-colors"
          >
            <div class="flex items-center gap-3 flex-1 min-w-0">
              <span
                class="inline-block px-3 py-1.5 rounded-md text-sm font-medium text-white flex-shrink-0"
                :style="{ backgroundColor: label.colorHex }"
              >
                {{ label.name }}
              </span>
              <span class="text-xs text-on-surface-variant truncate">{{ label.colorHex }}</span>
            </div>
            <div class="flex gap-1 flex-shrink-0">
              <button
                @click="startEditing(label)"
                class="p-1.5 text-on-surface-variant hover:text-primary hover:bg-primary/10 rounded transition-colors"
                title="Edit"
              >
                <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" />
                </svg>
              </button>
              <button
                @click="handleDeleteLabel(label)"
                class="p-1.5 text-on-surface-variant hover:text-error hover:bg-error/10 rounded transition-colors"
                title="Delete"
              >
                <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                </svg>
              </button>
            </div>
          </div>
        </div>

        <!-- Create Button -->
        <button
          v-if="!showLabelForm"
          @click="startCreating"
          class="w-full mb-4 px-4 py-2 text-sm font-medium text-primary hover:bg-primary/10 border border-primary/40 rounded-md transition-colors flex items-center justify-center gap-2"
        >
          <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 4v16m8-8H4" />
          </svg>
          Create New Label
        </button>

        <!-- Footer -->
        <div class="mt-6 pt-4 border-t border-outline-variant/30">
          <button
            @click="handleClose"
            type="button"
            class="w-full px-4 py-2 text-sm font-medium text-on-surface-variant hover:bg-surface-container-high border border-outline-variant/40 rounded-md transition-colors"
          >
            Done
          </button>
        </div>
      </div>
    </div>
  </div>
</template>
