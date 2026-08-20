<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'

const route = useRoute()
const attemptedPath = computed(() => route.path)
</script>

<template>
  <section class="paper-not-found" aria-labelledby="not-found-title">
    <p class="tk-eyebrow paper-not-found__eyebrow">404</p>
    <h1 id="not-found-title" class="tk-h2 paper-not-found__title">Page not found</h1>
    <p class="tk-lede paper-not-found__message">
      We couldn’t find <code class="paper-not-found__path">{{ attemptedPath }}</code>. Use one of these
      destinations to get back to your work.
    </p>
    <nav class="paper-not-found__actions" aria-label="Not found recovery">
      <RouterLink class="paper-not-found__action paper-not-found__action--ember" to="/workspace/home">
        Go to Home
      </RouterLink>
      <RouterLink class="paper-not-found__action" to="/workspace/boards">Open Boards</RouterLink>
    </nav>
  </section>
</template>

<style scoped>
/* ── Paper & Graphite — NotFoundView ──
   Styled against the Paper token system (--paper, --ink, --ember families).
   Tokens are defined under `.paper` / `.paper-night` (the canonical shell), so
   var() fallbacks keep the surface legible if this view is ever rendered
   outside the Paper shell (Legacy/Obsidian "off" mode). */

.paper-not-found {
  max-width: 42rem;
  margin: 0 auto;
  padding: var(--s-8, 32px);
  border-radius: var(--r-3, 6px);
  border: 1px solid var(--line, #d8d0bf);
  background: var(--paper-card, #fbf7ee);
  box-shadow: var(--shadow-card, 0 1px 0 #d8d0bf);
  font-family: var(--sans, system-ui, sans-serif);
  color: var(--ink, #1a1814);
}

.paper-not-found__eyebrow {
  margin: 0 0 var(--s-2, 8px);
  color: var(--mute, #635c4e);
}

.paper-not-found__title {
  margin: 0;
  font-size: var(--t-h2, 32px);
}

.paper-not-found__message {
  max-width: 34rem;
  margin: var(--s-3, 12px) 0 0;
  color: var(--ink-2, #3a352d);
}

.paper-not-found__path {
  font-family: var(--mono, ui-monospace, monospace);
  font-size: 0.95em;
  color: var(--ink-deep, #0a0908);
}

.paper-not-found__actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--s-3, 12px);
  margin-top: var(--s-6, 24px);
}

.paper-not-found__action {
  display: inline-flex;
  align-items: center;
  padding: var(--s-2, 8px) var(--s-4, 16px);
  border-radius: var(--r-2, 4px);
  border: 1px solid var(--line, #d8d0bf);
  background: var(--paper, #f3eee5);
  color: var(--ink, #1a1814);
  font-size: var(--t-md, 13.5px);
  font-weight: 600;
  text-decoration: none;
  transition: background var(--d-quick, 140ms) var(--ease-paper, ease),
    border-color var(--d-quick, 140ms) var(--ease-paper, ease);
}

.paper-not-found__action:hover {
  background: var(--paper-2, #ebe5d8);
  border-color: var(--ink-2, #3a352d);
}

.paper-not-found__action--ember {
  border-color: var(--ember, #a8421f);
  background: var(--ember, #a8421f);
  color: var(--td-on-ember, #fefaf6);
}

.paper-not-found__action--ember:hover {
  filter: brightness(1.1);
  background: var(--ember, #a8421f);
  border-color: var(--ember, #a8421f);
}
</style>
