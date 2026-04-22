<script setup lang="ts">
defineProps<{
  messageContent: string
  requestProposal: boolean
  sendingMessage: boolean
  lastMessageIsClarification: boolean
}>()

const emit = defineEmits<{
  (e: 'update:messageContent', value: string): void
  (e: 'update:requestProposal', value: boolean): void
  (e: 'send-message'): void
  (e: 'skip-clarification'): void
}>()
</script>

<template>
  <div class="td-chat-compose">
    <div v-if="lastMessageIsClarification" class="td-clarification-skip">
      <span class="td-clarification-skip__hint">The assistant is asking for more details.</span>
      <button
        class="td-btn td-btn--secondary td-btn--sm"
        :disabled="sendingMessage"
        @click="emit('skip-clarification')"
      >
        Skip, just do your best
      </button>
    </div>
    <textarea
      :value="messageContent"
      aria-label="Automation instruction"
      class="td-textarea"
      rows="3"
      placeholder="Describe an automation instruction..."
      @input="emit('update:messageContent', ($event.target as HTMLTextAreaElement).value)"
      @keydown.ctrl.enter.prevent="emit('send-message')"
    ></textarea>
    <label class="td-checkbox">
      <input
        :checked="requestProposal"
        type="checkbox"
        @change="emit('update:requestProposal', ($event.target as HTMLInputElement).checked)"
      />
      Request proposal generation
    </label>
    <button class="td-btn td-btn--primary" @click="emit('send-message')" :disabled="sendingMessage">
      {{ sendingMessage ? 'Sending...' : 'Send Message' }}
    </button>
  </div>
</template>

<style scoped>
.td-clarification-skip {
  display: flex;
  align-items: center;
  gap: var(--td-space-2);
  padding: var(--td-space-2);
  background: var(--td-bg-secondary, #f7fafc);
  border: 1px solid var(--td-color-info, #3182ce);
  border-radius: var(--td-radius, 6px);
}

.td-clarification-skip__hint {
  font-size: var(--td-font-sm);
  color: var(--td-text-secondary);
  flex: 1;
}

.td-chat-compose {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
}

.td-textarea {
  padding: var(--td-space-2) var(--td-space-3);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-md);
  font-size: var(--td-font-sm);
  resize: vertical;
}

.td-checkbox {
  display: inline-flex;
  align-items: center;
  gap: var(--td-space-1);
  font-size: var(--td-font-xs);
  color: var(--td-text-secondary);
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

.td-btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}
</style>
