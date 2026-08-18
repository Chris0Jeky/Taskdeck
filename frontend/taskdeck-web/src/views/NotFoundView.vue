<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'

const route = useRoute()
const attemptedPath = computed(() => route.path)
</script>

<template>
  <section class="td-not-found td-panel" aria-labelledby="not-found-title">
    <p class="td-not-found__eyebrow">404</p>
    <h1 id="not-found-title" class="td-page-title">Page not found</h1>
    <p class="td-not-found__message">
      We couldn’t find <code class="td-not-found__path">{{ attemptedPath }}</code>. Use one of these
      destinations to get back to your work.
    </p>
    <nav class="td-not-found__actions" aria-label="Not found recovery">
      <RouterLink class="td-btn td-btn--primary" to="/workspace/home">Go to Home</RouterLink>
      <RouterLink class="td-btn td-btn--secondary" to="/workspace/boards">Open Boards</RouterLink>
    </nav>
  </section>
</template>

<style scoped>
.td-not-found {
  max-width: 42rem;
  margin: 0 auto;
  padding: var(--td-space-8);
}

.td-not-found__eyebrow {
  margin: 0 0 var(--td-space-2);
  color: var(--td-text-tertiary);
  font-family: 'JetBrains Mono', monospace;
  font-size: var(--td-font-xs);
  font-weight: 700;
  letter-spacing: 0.16em;
}

.td-not-found__message {
  max-width: 34rem;
  margin: var(--td-space-3) 0 0;
  color: var(--td-text-secondary);
  line-height: 1.6;
}

.td-not-found__path {
  color: var(--td-text-primary);
  font-family: 'JetBrains Mono', monospace;
  font-size: 0.95em;
}

.td-not-found__actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--td-space-3);
  margin-top: var(--td-space-6);
}
</style>
