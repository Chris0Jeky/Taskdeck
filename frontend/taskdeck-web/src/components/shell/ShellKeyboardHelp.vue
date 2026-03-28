<script setup lang="ts">
defineProps<{
  visible: boolean
}>()

const emit = defineEmits<{
  close: []
}>()
</script>

<template>
  <Teleport to="body">
    <div
      v-if="visible"
      class="td-overlay"
      role="dialog"
      aria-label="Keyboard shortcuts"
      aria-modal="true"
      @click.self="emit('close')"
    >
      <div class="td-keyboard-help">
        <div class="td-keyboard-help__header">
          <h2>Keyboard Shortcuts</h2>
          <button aria-label="Close" @click="emit('close')">X</button>
        </div>
        <div class="td-keyboard-help__content">
          <div class="td-keyboard-help__section">
            <h3>Global</h3>
            <div class="td-shortcut-row"><kbd>Ctrl+K</kbd><span>Command palette</span></div>
            <div class="td-shortcut-row"><kbd>Ctrl+Shift+C</kbd><span>Quick capture modal</span></div>
            <div class="td-shortcut-row"><kbd>?</kbd><span>This help</span></div>
            <div class="td-shortcut-row"><kbd>Escape</kbd><span>Close top surface</span></div>
          </div>
          <div class="td-keyboard-help__section">
            <h3>Board Navigation</h3>
            <div class="td-shortcut-row"><kbd>h / Left</kbd><span>Previous column</span></div>
            <div class="td-shortcut-row"><kbd>l / Right</kbd><span>Next column</span></div>
            <div class="td-shortcut-row"><kbd>j / Down</kbd><span>Next card</span></div>
            <div class="td-shortcut-row"><kbd>k / Up</kbd><span>Previous card</span></div>
            <div class="td-shortcut-row"><kbd>Enter</kbd><span>Open card</span></div>
            <div class="td-shortcut-row"><kbd>n</kbd><span>New card</span></div>
            <div class="td-shortcut-row"><kbd>Shift+N</kbd><span>New column</span></div>
          </div>
          <div class="td-keyboard-help__section">
            <h3>Editor</h3>
            <div class="td-shortcut-row"><kbd>Ctrl+S</kbd><span>Save section</span></div>
            <div class="td-shortcut-row"><kbd>Ctrl+Enter</kbd><span>Save and close</span></div>
            <div class="td-shortcut-row"><kbd>Alt+1</kbd><span>Jump to title</span></div>
            <div class="td-shortcut-row"><kbd>Alt+2</kbd><span>Jump to description</span></div>
            <div class="td-shortcut-row"><kbd>Alt+4</kbd><span>Jump to labels</span></div>
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
  backdrop-filter: blur(24px);
  border: 0.5px solid rgba(91, 64, 62, 0.2);
  box-shadow: var(--td-shadow-lg);
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
  border-bottom: 1px solid rgba(91, 64, 62, 0.15);
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
  font-size: var(--td-font-lg);
  cursor: pointer;
  color: var(--td-text-tertiary);
  transition: color var(--td-transition-fast);
}

.td-keyboard-help__header button:hover {
  color: var(--td-color-ember);
}

.td-keyboard-help__content {
  padding: var(--td-space-5) var(--td-space-6);
}

.td-keyboard-help__section {
  margin-bottom: var(--td-space-6);
}

.td-keyboard-help__section h3 {
  font-family: 'Space Grotesk', system-ui, sans-serif;
  font-size: 10px;
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
  border: 0.5px solid rgba(91, 64, 62, 0.2);
  padding: 2px 8px;
  font-family: 'Space Grotesk', monospace;
  font-size: 10px;
  letter-spacing: 0.05em;
  color: var(--td-color-primary);
}
</style>
