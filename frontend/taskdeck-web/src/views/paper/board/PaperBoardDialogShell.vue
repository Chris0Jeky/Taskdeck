<script setup lang="ts">
import PaperKbd from '../../../components/paper/PaperKbd.vue'
import { useEscapeToClose } from '../../../composables/useEscapeToClose'

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
    @click.self="close"
  >
    <div class="paper-board-dialog">
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
  inset: 0;
  z-index: 60;
  background: rgba(26, 24, 20, 0.2);
  display: flex;
  align-items: flex-start;
  justify-content: center;
  padding: 60px 16px 16px;
  overflow-y: auto;
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
