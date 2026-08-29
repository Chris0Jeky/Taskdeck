<script setup lang="ts">
import {
  formatShortcut,
  KEYBOARD_HELP_SHORTCUT,
  PAPER_SHORTCUT_GROUPS,
} from '../../utils/keyboardShortcuts'

/**
 * ShellKeyboardHelp - the Legacy shell's `?` map (Paper renders
 * PaperShortcutsOverlay instead).
 *
 * Rows come from the shared ledger in `utils/keyboardShortcuts.ts`, the same
 * source PaperShortcutsOverlay renders, so the two surfaces cannot drift and
 * every row named here has a named handler owner. Modifier notation goes
 * through `formatShortcut` so Apple platforms see the Command glyph instead of
 * a hardcoded `Ctrl+` literal.
 *
 * Rows that no runtime implements (the former Editor section's Ctrl+S /
 * Ctrl+Enter / Alt+N jumps, and Shift+N "New column") were removed rather than
 * left advertising keys that do nothing.
 */
defineProps<{
  visible: boolean
}>()

const emit = defineEmits<{
  close: []
}>()
</script>

<template>
  <Teleport to="body">
    <!-- eslint-disable-next-line vuejs-accessibility/no-static-element-interactions -- modal backdrop with dialog role and escape key handler; click-to-close is standard modal UX -->
    <div
      v-if="visible"
      class="td-overlay"
      role="dialog"
      aria-label="Keyboard shortcuts"
      aria-modal="true"
      @click.self="emit('close')"
      @keydown.escape="emit('close')"
    >
      <div class="td-keyboard-help">
        <div class="td-keyboard-help__header">
          <h2>Keyboard Shortcuts</h2>
          <button aria-label="Close" @click="emit('close')">X</button>
        </div>
        <div class="td-keyboard-help__content">
          <div
            v-for="group in PAPER_SHORTCUT_GROUPS"
            :key="group.title"
            class="td-keyboard-help__section"
            :data-group="group.title"
          >
            <h3>{{ group.title }}</h3>
            <div
              v-for="row in group.rows"
              :key="row.id"
              class="td-shortcut-row"
              :data-shortcut-id="row.id"
            >
              <kbd>{{ formatShortcut(row.descriptor) }}</kbd>
              <span>{{ row.label }}<template v-if="row.note"> ({{ row.note }})</template></span>
            </div>
          </div>
          <div class="td-keyboard-help__section" data-group="Help">
            <h3>Help</h3>
            <div class="td-shortcut-row" data-shortcut-id="keyboard-help">
              <kbd>{{ formatShortcut(KEYBOARD_HELP_SHORTCUT.descriptor) }}</kbd><span>This help</span>
            </div>
            <div class="td-shortcut-row" data-shortcut-id="escape">
              <kbd>Escape</kbd><span>Close top surface</span>
            </div>
          </div>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.td-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.7);
  display: flex;
  align-items: flex-start;
  justify-content: center;
  padding-top: 15vh;
  z-index: 50;
  backdrop-filter: blur(4px);
}

.td-keyboard-help {
  background: var(--td-glass-bg);
  backdrop-filter: blur(var(--td-glass-blur));
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-lg);
  box-shadow: var(--td-shadow-xl);
  width: 100%;
  max-width: 520px;
  max-height: 80vh;
  overflow-y: auto;
}

.td-keyboard-help__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: var(--td-space-5) var(--td-space-6);
  border-bottom: 1px solid var(--td-border-default);
}

.td-keyboard-help__header h2 {
  font-family: 'Manrope', system-ui, sans-serif;
  font-size: var(--td-font-xl);
  font-weight: 800;
  letter-spacing: -0.02em;
  color: var(--td-text-primary);
}

.td-keyboard-help__header button {
  background: transparent;
  border: none;
  border-radius: var(--td-radius-md);
  font-size: var(--td-font-lg);
  cursor: pointer;
  padding: var(--td-space-1) var(--td-space-2);
  color: var(--td-text-tertiary);
  transition: color var(--td-transition-fast), background var(--td-transition-fast);
}

.td-keyboard-help__header button:hover {
  color: var(--td-color-ember);
  background: var(--td-surface-container-high);
}

.td-keyboard-help__header button:focus-visible {
  box-shadow: var(--td-focus-ring);
  outline: none;
}

.td-keyboard-help__content {
  padding: var(--td-space-5) var(--td-space-6);
}

.td-keyboard-help__section {
  margin-bottom: var(--td-space-6);
}

.td-keyboard-help__section h3 {
  font-family: 'Space Grotesk', system-ui, sans-serif;
  font-size: var(--td-font-xs);
  font-weight: 700;
  color: var(--td-color-ember);
  text-transform: uppercase;
  letter-spacing: 0.2em;
  margin-bottom: var(--td-space-3);
}

.td-shortcut-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: var(--td-space-2) 0;
  font-size: var(--td-font-sm);
  color: var(--td-text-muted);
}

.td-shortcut-row kbd {
  background: var(--td-surface-container-highest);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-sm);
  padding: var(--td-space-1) var(--td-space-3);
  font-family: 'Space Grotesk', monospace;
  font-size: var(--td-font-xs);
  letter-spacing: 0.05em;
  color: var(--td-color-primary);
}

.td-shortcut-hint {
  font-family: 'Space Grotesk', monospace;
  font-size: var(--td-font-xs);
  color: var(--td-text-tertiary);
  letter-spacing: 0.05em;
}
</style>
