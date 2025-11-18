<script setup lang="ts">
import { watch } from 'vue'

const props = defineProps<{
  isOpen: boolean
}>()

const emit = defineEmits<{
  (e: 'close'): void
}>()

// Close on Escape key
watch(() => props.isOpen, (isOpen) => {
  if (isOpen) {
    const handleEscape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        emit('close')
      }
    }
    window.addEventListener('keydown', handleEscape)

    return () => {
      window.removeEventListener('keydown', handleEscape)
    }
  }
})

function handleBackdropClick(event: MouseEvent) {
  if (event.target === event.currentTarget) {
    emit('close')
  }
}

interface Shortcut {
  keys: string[]
  description: string
}

interface ShortcutCategory {
  title: string
  shortcuts: Shortcut[]
}

const categories: ShortcutCategory[] = [
  {
    title: 'Navigation',
    shortcuts: [
      { keys: ['j', '↓'], description: 'Select next card' },
      { keys: ['k', '↑'], description: 'Select previous card' },
      { keys: ['h', '←'], description: 'Move to previous column' },
      { keys: ['l', '→'], description: 'Move to next column' },
    ]
  },
  {
    title: 'Actions',
    shortcuts: [
      { keys: ['Enter'], description: 'Open selected card' },
      { keys: ['n'], description: 'Create new card in current column' },
    ]
  },
  {
    title: 'General',
    shortcuts: [
      { keys: ['?'], description: 'Toggle this help dialog' },
      { keys: ['Esc'], description: 'Close dialog or cancel action' },
    ]
  }
]
</script>

<template>
  <Teleport to="body">
    <Transition name="modal">
      <div
        v-if="isOpen"
        class="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50 p-4"
        @click="handleBackdropClick"
      >
        <div class="bg-white rounded-lg shadow-xl max-w-2xl w-full max-h-[90vh] overflow-y-auto">
          <!-- Header -->
          <div class="sticky top-0 bg-white border-b border-gray-200 px-6 py-4 flex items-center justify-between">
            <div>
              <h2 class="text-xl font-bold text-gray-900">Keyboard Shortcuts</h2>
              <p class="text-sm text-gray-600 mt-1">Navigate and manage your boards faster</p>
            </div>
            <button
              @click="emit('close')"
              class="text-gray-400 hover:text-gray-600 transition-colors p-1 rounded hover:bg-gray-100"
              aria-label="Close"
            >
              <svg class="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
              </svg>
            </button>
          </div>

          <!-- Content -->
          <div class="px-6 py-6 space-y-8">
            <div
              v-for="category in categories"
              :key="category.title"
              class="space-y-3"
            >
              <h3 class="text-lg font-semibold text-gray-900 flex items-center gap-2">
                <span class="w-1 h-6 bg-blue-600 rounded"></span>
                {{ category.title }}
              </h3>
              <div class="space-y-2 ml-3">
                <div
                  v-for="(shortcut, index) in category.shortcuts"
                  :key="index"
                  class="flex items-center justify-between py-2 px-3 rounded hover:bg-gray-50 transition-colors"
                >
                  <span class="text-gray-700">{{ shortcut.description }}</span>
                  <div class="flex items-center gap-1">
                    <kbd
                      v-for="(key, keyIndex) in shortcut.keys"
                      :key="keyIndex"
                      class="px-2 py-1 text-sm font-semibold text-gray-800 bg-gray-100 border border-gray-300 rounded shadow-sm min-w-[2rem] text-center"
                    >
                      {{ key }}
                    </kbd>
                    <span v-if="keyIndex < shortcut.keys.length - 1" class="text-gray-400 text-sm">or</span>
                  </div>
                </div>
              </div>
            </div>
          </div>

          <!-- Footer -->
          <div class="sticky bottom-0 bg-gray-50 border-t border-gray-200 px-6 py-4">
            <div class="flex items-center justify-between">
              <p class="text-sm text-gray-600">
                Press <kbd class="px-2 py-1 text-xs font-semibold text-gray-800 bg-white border border-gray-300 rounded shadow-sm">?</kbd> anytime to show or hide this help
              </p>
              <button
                @click="emit('close')"
                class="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition-colors font-medium"
              >
                Got it!
              </button>
            </div>
          </div>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.modal-enter-active,
.modal-leave-active {
  transition: opacity 0.2s ease;
}

.modal-enter-from,
.modal-leave-to {
  opacity: 0;
}

.modal-enter-active .bg-white,
.modal-leave-active .bg-white {
  transition: transform 0.2s ease;
}

.modal-enter-from .bg-white,
.modal-leave-to .bg-white {
  transform: scale(0.95);
}

kbd {
  font-family: ui-monospace, SFMono-Regular, "SF Mono", Menlo, Consolas, "Liberation Mono", monospace;
}
</style>
