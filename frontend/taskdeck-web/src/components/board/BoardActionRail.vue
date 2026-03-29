<script setup lang="ts">
defineEmits<{
  capture: []
  chat: []
  review: []
  inbox: []
  addCard: []
}>()
</script>

<template>
  <div
    class="td-action-rail"
    data-board-action-rail
  >
    <span class="td-action-rail__label">Board Actions</span>
    <button class="td-action-rail__btn" @click="$emit('capture')">
      Capture here
    </button>
    <button class="td-action-rail__btn" @click="$emit('chat')">
      Ask assistant
    </button>
    <button class="td-action-rail__btn" @click="$emit('review')">
      Review proposals
    </button>
    <button class="td-action-rail__btn" @click="$emit('inbox')">
      Open Inbox
    </button>
    <button class="td-action-rail__btn td-action-rail__btn--primary" @click="$emit('addCard')">
      Add card
    </button>
    <p class="td-action-rail__hint">
      New input starts in Capture or Inbox. Only approved changes land on this board.
    </p>
  </div>
</template>

<style scoped>
.td-action-rail {
  margin-top: var(--td-space-5);
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--td-space-2);
  border-radius: var(--td-radius-xl);
  border: 0.5px solid var(--td-border-ghost);
  background: var(--td-surface-container-low);
  padding: var(--td-space-4) var(--td-space-5);
}

.td-action-rail__label {
  font-size: var(--td-font-xs);
  font-family: 'Space Grotesk', system-ui, sans-serif;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.1em;
  color: var(--td-text-tertiary);
}

.td-action-rail__btn {
  padding: var(--td-space-2) var(--td-space-4);
  font-size: var(--td-font-sm);
  font-weight: 500;
  color: var(--td-text-muted);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-lg);
  transition:
    background-color var(--td-transition-fast),
    color var(--td-transition-fast);
}

.td-action-rail__btn:hover {
  background: var(--td-surface-container);
  color: var(--td-text-primary);
}

.td-action-rail__btn:focus-visible {
  outline: none;
  box-shadow: var(--td-focus-ring);
}

.td-action-rail__btn--primary {
  background: var(--td-color-primary);
  color: var(--td-text-inverse);
  border-color: transparent;
}

.td-action-rail__btn--primary:hover {
  background: var(--td-color-primary-hover);
  color: var(--td-text-inverse);
}

.td-action-rail__hint {
  font-size: var(--td-font-sm);
  color: var(--td-text-tertiary);
}
</style>
