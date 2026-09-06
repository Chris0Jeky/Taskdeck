<script setup lang="ts">
import { ref } from 'vue'
import PaperKbd from '../../../components/paper/PaperKbd.vue'
import { useEscapeToClose } from '../../../composables/useEscapeToClose'
import { useDialogFocusManagement } from '../../../composables/useDialogFocusManagement'
import { useVisualViewport } from '../../../composables/useVisualViewport'

/**
 * PaperBoardDialogShell — the shared paper-card chrome for the two board
 * management dialogs (#1945): backdrop, hairline panel, eyebrow + title,
 * `esc` close affordance, body slot, footer slot.
 *
 * Escape is registered on the shared escape STACK, not on a plain window
 * listener, and that is load-bearing: the stack listens in the capture phase
 * and calls `stopPropagation()`, so an Escape aimed at this dialog can never
 * fall through to `BoardView`'s page-level `Escape` shortcut — which would
 * otherwise navigate away from the board while a dialog is open.
 *
 * Deliberately NOT teleported. `PaperShortcutsOverlay` teleports to `body`
 * because AppShell owns it; these dialogs belong to the board surface, and
 * keeping them in the view's own tree means a component spec can assert them
 * through the mounted wrapper.
 */
const props = defineProps<{
  isOpen: boolean
  eyebrow: string
  title: string
  closeLabel: string
  /** `data-testid` for the backdrop, so a spec can address one dialog by name. */
  testid?: string
}>()

const emit = defineEmits<{ (event: 'close'): void }>()

function close() {
  emit('close')
}

useEscapeToClose(() => props.isOpen, close)

/**
 * Focus on open, focus back on close (GH-1959).
 *
 * Opening a modal that leaves focus on the button behind it is how a keystroke
 * aimed at the dialog reaches the board instead. Focus entry, Tab trapping, and
 * restore now use the same composable as `TdDialog.vue`; visual-viewport sizing
 * uses the same shared viewport observer.
 *
 * The shared restore runs from `onUnmounted` as well as the watcher because these
 * dialogs are `v-if`-ed by the parent on the same state as `isOpen`: closing
 * usually destroys the component before the watcher can see `false`. Whichever
 * path runs first clears the reference, so the other is a no-op.
 */
const dialogRef = ref<HTMLElement | null>(null)
const { trapFocus } = useDialogFocusManagement({
  isOpen: () => props.isOpen,
  dialogRef,
})

// Match the visual viewport so a software keyboard cannot cover footer actions.
// The unset fallback leaves the guarded 100dvh/100vh CSS chain below intact.
const { style: visualViewportStyle } = useVisualViewport({
  prefix: '--paper-board-dialog',
  fallback: 'unset',
})
</script>

<template>
  <!-- eslint-disable-next-line vuejs-accessibility/no-static-element-interactions, vuejs-accessibility/click-events-have-key-events -- modal backdrop with dialog role; Escape closes via the shared escape stack, click-to-close is standard modal UX -->
  <div
    v-if="isOpen"
    class="paper-board-dialog__backdrop"
    role="dialog"
    aria-modal="true"
    :aria-label="title"
    :data-testid="testid"
    :style="visualViewportStyle"
    @click.self="close"
    @keydown="trapFocus"
  >
    <!-- `tabindex="-1"` is what makes the panel focusable on open; it stays out
         of the Tab order itself. -->
    <div ref="dialogRef" class="paper-board-dialog" tabindex="-1">
      <header class="paper-board-dialog__head">
        <div class="paper-board-dialog__head-text">
          <span class="tk-eyebrow">{{ eyebrow }}</span>
          <h2 class="tk-h2 paper-board-dialog__title">{{ title }}</h2>
        </div>
        <button
          type="button"
          class="paper-board-dialog__close"
          :aria-label="closeLabel"
          data-action="close-dialog"
          @click="close"
        >
          <PaperKbd>esc</PaperKbd>
        </button>
      </header>

      <div class="paper-board-dialog__body">
        <slot />
      </div>

      <footer class="paper-board-dialog__foot">
        <slot name="footer" />
      </footer>
    </div>
  </div>
</template>

<style scoped>
.paper-board-dialog__backdrop {
  position: fixed;
  left: 0;
  right: 0;
  top: var(--paper-board-dialog-visual-viewport-offset-top, 0px);
  /* Three declarations, deliberately, one per browser class. `top` above is
   * consumed unconditionally, so `height` must be too: a browser that has the
   * VisualViewport API but not `dvh` would otherwise take the offset while
   * staying `100vh` tall, and the sheet would hang off the bottom of the screen
   * by exactly `offsetTop` with its footer under the software keyboard.
   *
   *   1. No custom properties at all -> `var()` does not parse, the next two
   *      declarations are dropped at parse time, and this floor stands.
   *   2. Custom properties, no `dvh` -> the live visual-viewport height, or
   *      `100vh` when `useVisualViewport`'s `'unset'` fallback emits nothing.
   *   3. `dvh` too -> same, with the fallback upgraded to `100dvh` below.
   *
   * The fallback inside `var()` here MUST be `100vh`, never `100dvh`: `var()`
   * parses in every browser with custom properties, so an unguarded
   * `100dvh` fallback would substitute a value that is invalid at
   * computed-value time on a `dvh`-less browser. Per the CSS Variables spec
   * this non-inherited property would then compute to its INITIAL value
   * (`auto`) and the discarded floor would not resurface. */
  height: 100vh;
  height: var(--paper-board-dialog-visual-viewport-height, 100vh);
  box-sizing: border-box;
  z-index: 60;
  background: rgba(26, 24, 20, 0.2);
  display: flex;
  align-items: flex-start;
  justify-content: center;
  padding: 60px 16px 16px;
  overflow-y: auto;
}

/* Upgrade the FALLBACK only. The visual-viewport binding itself is already
 * unconditional above; this block exists so a browser with `dvh` but no
 * VisualViewport API gets `100dvh` (which survives browser-chrome collapse)
 * instead of `100vh`. It carries no extra specificity, so it wins on source
 * order alone. */
@supports (height: 100dvh) {
  .paper-board-dialog__backdrop {
    height: var(--paper-board-dialog-visual-viewport-height, 100dvh);
  }
}

.paper-board-dialog {
  width: min(460px, 100%);
  background: var(--paper-card);
  border: 1px solid var(--line);
  border-radius: var(--r-2);
  font-family: var(--sans);
  color: var(--ink);
}

.paper-board-dialog__head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
  padding: 16px 20px;
  border-bottom: 1px solid var(--line);
}

.paper-board-dialog__head-text {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}

.paper-board-dialog__title {
  margin: 0;
}

.paper-board-dialog__close {
  background: transparent;
  border: none;
  padding: 0;
  cursor: pointer;
  flex: none;
}

.paper-board-dialog__body {
  padding: 16px 20px;
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.paper-board-dialog__foot {
  padding: 12px 20px;
  border-top: 1px solid var(--line);
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  flex-wrap: wrap;
}
</style>
