<script setup lang="ts">
import type { ParsedHintMessage } from '../../utils/chat'

defineProps<{
  hint: ParsedHintMessage
  messageId: string
  expanded: boolean
}>()

const emit = defineEmits<{
  (e: 'apply-suggestion', example: string): void
  (e: 'toggle-patterns', messageId: string): void
}>()
</script>

<template>
  <div class="td-hint-card" role="region" aria-label="Instruction format hint">
    <div class="td-hint-card__header">
      <span class="td-hint-card__icon" aria-hidden="true">i</span>
      <span class="td-hint-card__title">
        {{ hint.hint.detectedIntent
          ? `Detected intent: ${hint.hint.detectedIntent}`
          : 'Could not detect intent' }}
      </span>
    </div>
    <p class="td-hint-card__suggestion">
      Try this format:
      <code>{{ hint.hint.closestPattern }}</code>
    </p>
    <div class="td-hint-card__actions">
      <button
        class="td-btn td-btn--primary td-btn--sm"
        @click="emit('apply-suggestion', hint.hint.exampleInstruction)"
      >
        Try this instead
      </button>
      <button
        class="td-btn td-btn--secondary td-btn--sm"
        :aria-expanded="expanded"
        @click="emit('toggle-patterns', messageId)"
      >
        {{ expanded ? 'Hide all patterns' : 'Show all patterns' }}
      </button>
    </div>
    <ul
      v-if="expanded"
      class="td-hint-card__patterns"
      aria-label="Supported instruction patterns"
    >
      <li
        v-for="pattern in hint.hint.supportedPatterns"
        :key="pattern"
      >
        <code>{{ pattern }}</code>
      </li>
    </ul>
  </div>
</template>

<style scoped>
.td-hint-card {
  margin-top: var(--td-space-2);
  border: 1px solid var(--td-color-primary, #3182ce);
  border-radius: var(--td-radius-md);
  padding: var(--td-space-3);
  background: color-mix(in srgb, var(--td-surface-primary) 90%, #ebf4ff 10%);
}

.td-hint-card__header {
  display: flex;
  align-items: center;
  gap: var(--td-space-2);
  margin-bottom: var(--td-space-2);
}

.td-hint-card__icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 20px;
  height: 20px;
  border-radius: 50%;
  background: var(--td-color-primary, #3182ce);
  color: var(--td-text-inverse, #fff);
  font-size: 12px;
  font-weight: 700;
  flex-shrink: 0;
}

.td-hint-card__title {
  font-size: var(--td-font-sm);
  font-weight: 600;
  color: var(--td-text-primary);
}

.td-hint-card__suggestion {
  margin: 0 0 var(--td-space-2);
  font-size: var(--td-font-sm);
  color: var(--td-text-secondary);
}

.td-hint-card__suggestion code {
  font-family: monospace;
  font-size: 0.9em;
  background: var(--td-surface-tertiary);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-sm);
  padding: 1px 4px;
}

.td-hint-card__actions {
  display: flex;
  gap: var(--td-space-2);
  flex-wrap: wrap;
}

.td-hint-card__patterns {
  margin: var(--td-space-2) 0 0;
  padding-left: var(--td-space-4);
  list-style: disc;
}

.td-hint-card__patterns li {
  margin-bottom: var(--td-space-1);
  font-size: var(--td-font-sm);
}

.td-hint-card__patterns code {
  font-family: monospace;
  font-size: 0.9em;
  background: var(--td-surface-tertiary);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-sm);
  padding: 1px 4px;
}

.td-btn {
  padding: var(--td-space-2) var(--td-space-4);
  border: none;
  border-radius: var(--td-radius-md);
  font-size: var(--td-font-sm);
  font-weight: 600;
  cursor: pointer;
}

.td-btn--sm {
  padding: var(--td-space-1) var(--td-space-3);
  font-size: var(--td-font-xs);
}

.td-btn--primary {
  background: var(--td-color-primary);
  color: var(--td-text-inverse);
}

.td-btn--primary:hover:not(:disabled) {
  background: var(--td-color-primary-hover);
}

.td-btn--secondary {
  background: var(--td-surface-tertiary);
  color: var(--td-text-primary);
  border: 1px solid var(--td-border-default);
}

.td-btn--secondary:hover:not(:disabled) {
  background: var(--td-surface-hover);
}
</style>
