<script setup lang="ts">
import { useEscapeToClose } from '../composables/useEscapeToClose'

const props = defineProps<{
  isOpen: boolean
}>()

const emit = defineEmits<{
  (e: 'close'): void
}>()

useEscapeToClose(() => props.isOpen, () => emit('close'))

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
      { keys: ['j', 'ArrowDown'], description: 'Select next card' },
      { keys: ['k', 'ArrowUp'], description: 'Select previous card' },
      { keys: ['h', 'ArrowLeft'], description: 'Move to previous column' },
      { keys: ['l', 'ArrowRight'], description: 'Move to next column' },
    ]
  },
  {
    title: 'Card Movement',
    shortcuts: [
      { keys: ['Alt + ArrowRight'], description: 'Move card to next column' },
      { keys: ['Alt + ArrowLeft'], description: 'Move card to previous column' },
      { keys: ['Alt + ArrowUp'], description: 'Move card up in column' },
      { keys: ['Alt + ArrowDown'], description: 'Move card down in column' },
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
      { keys: ['f'], description: 'Toggle filter panel' },
      { keys: ['Esc'], description: 'Close dialog or cancel action' },
    ]
  }
]
</script>

<template>
  <Teleport to="body">
    <Transition name="modal">
      <!-- eslint-disable-next-line vuejs-accessibility/no-static-element-interactions -- modal backdrop: dialog role with keyboard handler satisfies a11y, click-to-close is standard UX -->
      <div
        v-if="isOpen"
        class="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50 p-4"
        role="dialog"
        aria-label="Keyboard Shortcuts"
        aria-modal="true"
        @click="handleBackdropClick"
        @keydown.escape="emit('close')"
      >
        <div class="kbd-help-panel bg-surface-container rounded-lg shadow-xl max-w-2xl w-full max-h-[90vh] overflow-y-auto border border-outline-variant/30">
          <!-- Header -->
          <div class="sticky top-0 bg-surface-container border-b border-outline-variant/30 px-6 py-4 flex items-center justify-between">
            <div>
              <h2 class="text-xl font-bold text-on-surface">Keyboard Shortcuts</h2>
              <p class="text-sm text-on-surface-variant mt-1">Navigate and manage your boards faster</p>
            </div>
            <button
              @click="emit('close')"
              class="text-on-surface-variant hover:text-on-surface transition-colors p-1 rounded hover:bg-surface-container-high"
              aria-label="Close"
            >
              <svg class="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
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
              <h3 class="text-lg font-semibold text-on-surface flex items-center gap-2">
                <span class="w-1 h-6 bg-primary rounded"></span>
                {{ category.title }}
              </h3>
              <div class="space-y-2 ml-3">
                <div
                  v-for="shortcut in category.shortcuts"
                  :key="`${category.title}-${shortcut.description}`"
                  class="flex items-center justify-between py-2 px-3 rounded hover:bg-surface-container-high transition-colors"
                >
                  <span class="text-on-surface-variant">{{ shortcut.description }}</span>
                  <div class="flex items-center gap-1">
                    <template v-for="(key, keyIndex) in shortcut.keys" :key="`${shortcut.description}-${keyIndex}-${key}`">
                      <kbd
                        class="px-2 py-1 text-sm font-semibold text-on-surface bg-surface-container-high border border-outline-variant/40 rounded shadow-sm min-w-[2rem] text-center"
                      >
                        {{ key }}
                      </kbd>
                      <span v-if="keyIndex < shortcut.keys.length - 1" class="text-on-surface-variant text-sm">or</span>
                    </template>
                  </div>
                </div>
              </div>
            </div>
          </div>

          <!-- Footer -->
          <div class="sticky bottom-0 bg-surface-container-low border-t border-outline-variant/30 px-6 py-4">
            <div class="flex items-center justify-between">
              <p class="text-sm text-on-surface-variant">
                Press <kbd class="px-2 py-1 text-xs font-semibold text-on-surface bg-surface-container-high border border-outline-variant/40 rounded shadow-sm">?</kbd> anytime to show or hide this help
              </p>
              <button
                @click="emit('close')"
                class="px-4 py-2 bg-primary-container text-on-primary-container rounded-lg hover:brightness-110 transition-all font-medium"
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

.modal-enter-active .kbd-help-panel,
.modal-leave-active .kbd-help-panel {
  transition: transform 0.2s ease;
}

.modal-enter-from .kbd-help-panel,
.modal-leave-to .kbd-help-panel {
  transform: scale(0.95);
}

kbd {
  font-family: ui-monospace, SFMono-Regular, "SF Mono", Menlo, Consolas, "Liberation Mono", monospace;
}
</style>
