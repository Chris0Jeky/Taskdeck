<script setup lang="ts">
/**
 * ReviewWhyNow — "Why now" explanatory card. The body comes from the
 * orchestrator (ultimately the proposal payload).
 *
 * NO "tune heuristics" link (#1941). The card used to end in
 * `<a href="#">Tune heuristics →</a>` with no handler and no route: a dead
 * link. There is nothing to point it at either. Confidence is evidence shown
 * during review, never a setting or threshold that authorizes Apply.
 */
defineProps<{
  body: string
}>()
</script>

<template>
  <section class="card paper-review-whynow">
    <div class="tk-eyebrow paper-review-whynow__eyebrow">{{ $t('review.whyNow.heading') }}</div>
    <p class="paper-review-whynow__body">{{ body }}</p>
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
</style>
