<script setup lang="ts">
defineEmits<{
  (e: 'open-inbox'): void
  (e: 'navigate', path: string): void
}>()
</script>

<template>
  <section class="td-panel td-review-empty">
    <h2 class="td-section-title">No proposals need review yet</h2>
    <p class="td-section-desc">
      Start from Inbox when you want Taskdeck to propose a change, or open Boards if you want to continue directly
      with the work that already landed.
    </p>
    <div class="td-review-empty__actions">
      <button class="td-btn td-btn--primary" @click="$emit('open-inbox')">Go to Inbox</button>
      <button class="td-btn td-btn--secondary" @click="$emit('navigate', '/workspace/boards')">Open Boards</button>
      <button class="td-btn td-btn--secondary" @click="$emit('navigate', '/workspace/home')">Back to Home</button>
    </div>
  </section>
</template>

<style scoped>
.td-review-empty {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-3);
}

.td-review-empty__actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--td-space-2);
}

@media (max-width: 640px) {
  .td-review-empty__actions {
    flex-direction: column;
  }

  .td-review-empty__actions .td-btn {
    width: 100%;
    min-height: 44px;
    justify-content: center;
  }
}
</style>
