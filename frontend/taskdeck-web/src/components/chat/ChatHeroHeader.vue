<script setup lang="ts">
defineProps<{
  loadingHealth: boolean
}>()

const emit = defineEmits<{
  (e: 'refresh-health'): void
  (e: 'verify-llm'): void
  (e: 'open-review'): void
  (e: 'open-queue'): void
}>()
</script>

<template>
  <header class="td-panel td-chat__hero">
    <div class="td-chat__hero-copy">
      <span class="td-chat__eyebrow">Advanced</span>
      <h1 class="td-page-title">Automation Chat</h1>
      <p class="td-chat__subtitle">
        Use chat when you need to inspect or refine automation conversations manually. Proposal decisions still belong
        in Review, which remains the normal path.
      </p>
    </div>

    <div class="td-chat__hero-actions">
      <button class="td-btn td-btn--secondary" :disabled="loadingHealth" @click="emit('refresh-health')">
        {{ loadingHealth ? 'Checking provider...' : 'Refresh LLM Status' }}
      </button>
      <button class="td-btn td-btn--secondary" :disabled="loadingHealth" @click="emit('verify-llm')">
        {{ loadingHealth ? 'Probing...' : 'Verify LLM' }}
      </button>
      <button class="td-btn td-btn--primary" @click="emit('open-review')">Back to Review</button>
      <button class="td-btn td-btn--secondary" @click="emit('open-queue')">
        Open Queue (Advanced)
      </button>
    </div>
  </header>
</template>

<style scoped>
.td-page-title {
  font-size: var(--td-font-2xl);
  font-weight: 700;
  color: var(--td-text-primary);
}

.td-chat__hero {
  display: flex;
  justify-content: space-between;
  gap: var(--td-space-6);
  align-items: flex-start;
}

.td-chat__hero-copy {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
  max-width: 720px;
}

.td-chat__eyebrow {
  font-size: var(--td-font-xs);
  font-weight: 700;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: var(--td-color-primary);
}

.td-chat__subtitle {
  color: var(--td-text-secondary);
  line-height: 1.6;
}

.td-chat__hero-actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--td-space-2);
  justify-content: flex-end;
}

.td-btn {
  padding: var(--td-space-2) var(--td-space-4);
  border: none;
  border-radius: var(--td-radius-md);
  font-size: var(--td-font-sm);
  font-weight: 600;
  cursor: pointer;
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

@media (max-width: 900px) {
  .td-chat__hero {
    flex-direction: column;
  }
}
</style>
