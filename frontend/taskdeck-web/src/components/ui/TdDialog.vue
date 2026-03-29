<script setup lang="ts">
import { ref, watch, nextTick, onUnmounted } from 'vue'
import { registerEscapeHandler } from '../../composables/useEscapeStack'

const props = withDefaults(
  defineProps<{
    open: boolean
    title?: string
    description?: string
    closeOnBackdrop?: boolean
  }>(),
  {
    title: '',
    description: '',
    closeOnBackdrop: true,
  },
)

const emit = defineEmits<{
  close: []
}>()

const dialogRef = ref<HTMLElement | null>(null)
let previouslyFocusedElement: HTMLElement | null = null
let unregisterEscape: (() => void) | null = null

function requestClose() {
  emit('close')
}

function handleBackdropClick() {
  if (props.closeOnBackdrop) {
    requestClose()
  }
}

function trapFocus(event: KeyboardEvent) {
  if (event.key !== 'Tab' || !dialogRef.value) {
    return
  }

  const focusableSelector =
    'a[href], button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex]:not([tabindex="-1"])'
  const focusableElements = Array.from(
    dialogRef.value.querySelectorAll<HTMLElement>(focusableSelector),
  )

  if (focusableElements.length === 0) {
    event.preventDefault()
    return
  }

  const first = focusableElements[0]!
  const last = focusableElements[focusableElements.length - 1]!

  if (event.shiftKey) {
    if (document.activeElement === first) {
      event.preventDefault()
      last.focus()
    }
  } else {
    if (document.activeElement === last) {
      event.preventDefault()
      first.focus()
    }
  }
}

watch(
  () => props.open,
  async (isOpen) => {
    if (isOpen) {
      previouslyFocusedElement = document.activeElement as HTMLElement | null
      unregisterEscape = registerEscapeHandler(requestClose)
      await nextTick()
      dialogRef.value?.focus()
    } else {
      unregisterEscape?.()
      unregisterEscape = null
      previouslyFocusedElement?.focus()
      previouslyFocusedElement = null
    }
  },
  { immediate: true },
)

onUnmounted(() => {
  unregisterEscape?.()
  unregisterEscape = null
})
</script>

<template>
  <Teleport to="body">
    <Transition name="td-dialog">
      <div
        v-if="props.open"
        class="td-dialog-backdrop"
        @click.self="handleBackdropClick"
      >
        <div
          ref="dialogRef"
          class="td-dialog"
          role="dialog"
          :aria-modal="true"
          :aria-label="props.title || undefined"
          :aria-describedby="props.description ? 'td-dialog-desc' : undefined"
          tabindex="-1"
          @keydown="trapFocus"
        >
          <header v-if="props.title || $slots.header" class="td-dialog__header">
            <slot name="header">
              <h2 class="td-dialog__title">{{ props.title }}</h2>
            </slot>
          </header>

          <p v-if="props.description" id="td-dialog-desc" class="td-dialog__description">
            {{ props.description }}
          </p>

          <div class="td-dialog__body">
            <slot />
          </div>

          <footer v-if="$slots.footer" class="td-dialog__footer">
            <slot name="footer" />
          </footer>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.td-dialog-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 60;
  padding: var(--td-space-4);
}

.td-dialog {
  width: min(560px, 100%);
  max-height: calc(100vh - 2 * var(--td-space-8));
  overflow-y: auto;
  background: var(--td-surface-container);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-xl);
  box-shadow: var(--td-shadow-xl);
  padding: var(--td-space-5);
  display: flex;
  flex-direction: column;
  gap: var(--td-space-3);
}

.td-dialog:focus {
  outline: none;
}

.td-dialog__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.td-dialog__title {
  font-size: var(--td-font-xl);
  font-weight: 600;
  color: var(--td-text-primary);
  margin: 0;
}

.td-dialog__description {
  font-size: var(--td-font-sm);
  color: var(--td-text-secondary);
  margin: 0;
}

.td-dialog__body {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-3);
}

.td-dialog__footer {
  display: flex;
  justify-content: flex-end;
  gap: var(--td-space-2);
  padding-top: var(--td-space-3);
  border-top: 1px solid var(--td-border-ghost);
}

/* ── Transition ── */
.td-dialog-enter-active,
.td-dialog-leave-active {
  transition: opacity var(--td-transition-normal);
}

.td-dialog-enter-active .td-dialog,
.td-dialog-leave-active .td-dialog {
  transition: transform var(--td-transition-smooth), opacity var(--td-transition-normal);
}

.td-dialog-enter-from,
.td-dialog-leave-to {
  opacity: 0;
}

.td-dialog-enter-from .td-dialog {
  transform: scale(0.95) translateY(8px);
}

.td-dialog-leave-to .td-dialog {
  transform: scale(0.95) translateY(8px);
}
</style>
