<script setup lang="ts">
/**
 * ReviewWhyNow — "Why now" explanatory card. The body comes from the
 * orchestrator (ultimately the proposal payload). Includes a tune-heuristics
 * link styled in ember.
 */
defineProps<{
  body: string
  tuneHref?: string
}>()
</script>

<template>
  <section class="card paper-review-whynow">
    <div class="tk-eyebrow paper-review-whynow__eyebrow">Why now</div>
    <p class="paper-review-whynow__body">{{ body }}</p>
    <a
      :href="tuneHref ?? '#'"
      class="paper-review-whynow__link"
    >Tune heuristics →</a>
  </section>
</template>

<style scoped>
.paper-review-whynow {
  padding: 14px;
  margin-top: 12px;
}
.paper-review-whynow__eyebrow {
  margin-bottom: 8px;
}
.paper-review-whynow__body {
  margin: 0;
  font-size: 12.5px;
  color: var(--ink-2, var(--ink));
}
.paper-review-whynow__link {
  display: inline-block;
  margin-top: 8px;
  font-family: var(--mono);
  font-size: 11px;
  color: var(--ember);
  text-decoration: none;
  border-bottom: 1px solid var(--ember);
}
</style>
