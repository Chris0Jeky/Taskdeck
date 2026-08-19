<script setup lang="ts">
import { ref, watch, nextTick, onUnmounted } from 'vue'
import { registerEscapeHandler } from '../../composables/useEscapeStack'
import { useVisualViewport } from '../../composables/useVisualViewport'

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

// The backdrop teleports to <body>, so no ancestor can constrain it — a software
// keyboard would otherwise leave the footer actions underneath itself. The
// `'unset'` fallback keeps the `100dvh` mobile sheet intact on browsers without
// a VisualViewport API (see the `var(..., 100dvh)` declarations below).
const { style: visualViewportStyle } = useVisualViewport({
  prefix: '--td-dialog',
  fallback: 'unset',
})

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
  previouslyFocusedElement?.focus()
  previouslyFocusedElement = null
})
</script>

<template>
  <Teleport to="body">
    <Transition name="td-dialog">
      <!-- eslint-disable-next-line vuejs-accessibility/no-static-element-interactions -- backdrop container; escape key handled here, dialog element inside receives focus -->
      <div
        v-if="props.open"
        class="td-dialog-backdrop"
        :style="visualViewportStyle"
        @click.self="handleBackdropClick"
        @keydown.escape="handleBackdropClick"
      >
        <!-- eslint-disable-next-line vuejs-accessibility/no-static-element-interactions -- dialog element with focus trap; @keydown handles tab cycling and escape -->
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
  /* Bound to the VISUAL viewport, not the layout viewport: a software keyboard
   * contracts the visual viewport only, and `inset: 0` would keep the dialog
   * (and its footer actions) spanning the full layout viewport underneath it.
   * `--td-dialog-visual-viewport-*` come from `useVisualViewport`; when the
   * browser has no VisualViewport API they are never set and the `100dvh`
   * fallback below applies. */
  left: 0;
  right: 0;
  top: var(--td-dialog-visual-viewport-offset-top, 0px);
  /* vh fallback for browsers without custom properties (they drop the next
   * declaration outright); those browsers also predate dvh. */
  height: 100vh;
  height: var(--td-dialog-visual-viewport-height, 100dvh);
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
  max-height: calc(var(--td-dialog-visual-viewport-height, 100dvh) - 2 * var(--td-space-8));
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

/* ── Mobile: full-screen dialog ── */
@media (max-width: 640px) {
  .td-dialog-backdrop {
    padding: 0;
    align-items: stretch;
  }

  .td-dialog {
    width: 100%;
    max-width: 100%;
    /* vh fallback for iOS Safari <= 15.4 which doesn't support dvh; dvh
     * handles browser chrome collapse so the close/footer stays reachable
     * even when the URL bar is visible. */
    max-height: 100vh;
    height: 100vh;
    max-height: 100dvh;
    height: 100dvh;
    /* The backdrop is already sized to the visual viewport (or 100dvh when the
     * VisualViewport API is missing), so fill it rather than re-deriving a
     * layout-viewport height here. */
    max-height: 100%;
    height: 100%;
    border-radius: 0;
    /* Respect iOS safe-area insets so footer actions don't sit under the
     * home indicator and the header doesn't collide with the notch. */
    padding: max(var(--td-space-4), env(safe-area-inset-top))
      max(var(--td-space-4), env(safe-area-inset-right))
      max(var(--td-space-4), env(safe-area-inset-bottom))
      max(var(--td-space-4), env(safe-area-inset-left));
  }

  .td-dialog__footer {
    /* Stack footer actions and keep tap targets at 44px.
     *
     * Use `column` (not `column-reverse`) so the visual order matches the
     * DOM/tab order (WCAG 2.4.3 Focus Order). Slots emit secondary-then-
     * primary actions (e.g. [Cancel, Delete]); `column-reverse` would show
     * the primary on top but keyboard focus would still start on the
     * secondary below it, confusing assistive tech users. */
    flex-direction: column;
    gap: var(--td-space-2);
  }

  .td-dialog__footer :deep(> *) {
    width: 100%;
    min-height: 44px;
  }
}
</style>
