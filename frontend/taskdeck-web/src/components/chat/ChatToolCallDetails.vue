<script setup lang="ts">
import type { ToolCallMetadata } from '../../types/chat'

defineProps<{
  metadata: ToolCallMetadata
  messageId: string
  expanded: boolean
}>()

const emit = defineEmits<{
  (e: 'toggle', messageId: string): void
}>()

function formatToolName(toolName: string): string {
  return toolName.replace(/_/g, ' ').replace(/\b\w/g, (c) => c.toUpperCase())
}
</script>

<template>
  <div class="td-tool-meta">
    <button
      class="td-tool-meta__toggle"
      :aria-expanded="expanded"
      @click="emit('toggle', messageId)"
    >
      <span class="td-tool-meta__icon" aria-hidden="true">{{ expanded ? '&#9660;' : '&#9654;' }}</span>
      {{ metadata.tool_calls.length }} tool call{{ metadata.tool_calls.length === 1 ? '' : 's' }} in {{ metadata.rounds }} round{{ metadata.rounds === 1 ? '' : 's' }}
    </button>
    <div v-if="expanded" class="td-tool-meta__details">
      <div
        v-for="(call, idx) in metadata.tool_calls"
        :key="idx"
        class="td-tool-call"
        :class="{ 'td-tool-call--error': call.is_error }"
      >
        <div class="td-tool-call__header">
          <span class="td-tool-call__round">R{{ call.round }}</span>
          <span class="td-tool-call__name">{{ formatToolName(call.tool) }}</span>
          <span v-if="call.is_error" class="td-tool-call__badge td-tool-call__badge--error">Error</span>
          <span v-else-if="call.tool.startsWith('propose_')" class="td-tool-call__badge td-tool-call__badge--proposal">Proposal</span>
        </div>
        <div class="td-tool-call__summary">{{ call.result_summary }}</div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.td-tool-meta {
  margin-top: var(--td-space-2);
  border-top: 1px solid var(--td-border-default);
  padding-top: var(--td-space-1);
}

.td-tool-meta__toggle {
  background: none;
  border: none;
  cursor: pointer;
  font-size: var(--td-font-xs);
  color: var(--td-text-tertiary);
  padding: var(--td-space-1) 0;
  display: flex;
  align-items: center;
  gap: var(--td-space-1);
}

.td-tool-meta__toggle:hover {
  color: var(--td-text-secondary);
}

.td-tool-meta__icon {
  font-size: 8px;
  line-height: 1;
}

.td-tool-meta__details {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-1);
  margin-top: var(--td-space-1);
}

.td-tool-call {
  padding: var(--td-space-1) var(--td-space-2);
  border-left: 2px solid var(--td-border-default);
  font-size: var(--td-font-xs);
}

.td-tool-call--error {
  border-left-color: var(--td-color-danger, #e53e3e);
}

.td-tool-call__header {
  display: flex;
  align-items: center;
  gap: var(--td-space-1);
  margin-bottom: 2px;
}

.td-tool-call__round {
  font-weight: 700;
  color: var(--td-text-tertiary);
  min-width: 20px;
}

.td-tool-call__name {
  font-weight: 600;
  color: var(--td-text-primary);
}

.td-tool-call__badge {
  font-size: 10px;
  font-weight: 700;
  padding: 1px 4px;
  border-radius: var(--td-radius-sm);
}

.td-tool-call__badge--error {
  background: color-mix(in srgb, var(--td-surface-primary) 80%, #fed7d7 20%);
  color: var(--td-color-danger, #e53e3e);
}

.td-tool-call__badge--proposal {
  background: color-mix(in srgb, var(--td-surface-primary) 80%, #c6f6d5 20%);
  color: var(--td-color-success, #2f855a);
}

.td-tool-call__summary {
  color: var(--td-text-secondary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  max-width: 400px;
}
</style>
