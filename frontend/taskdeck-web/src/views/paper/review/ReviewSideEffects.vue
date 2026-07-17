<script setup lang="ts">
import type { SideEffects } from '../../../composables/usePaperReviewSelectors'

/**
 * ReviewSideEffects — § III: 7-row table of what lands/doesn't land plus a
 * factual apply-risk card. Both the rows and the risk data are driven from
 * `usePaperReviewSelectors`; no recovery action is implied.
 */
defineProps<{ data: SideEffects }>()
</script>

<template>
  <section class="paper-review-se">
    <header class="paper-review-se__header">
      <span class="tk-serial paper-review-se__serial">§ III</span>
      <h3 class="tk-h3 paper-review-se__title">Side effects</h3>
      <span class="tk-meta paper-review-se__sub">What lands · what doesn't · what archives</span>
    </header>
    <div class="paper-review-se__grid">
      <div class="card paper-review-se__rows">
        <div
          v-for="row in data.rows"
          :key="row.key"
          class="paper-review-se__row"
          :data-tone="row.tone"
        >
          <span class="tk-eyebrow paper-review-se__row-key">{{ row.key }}</span>
          <span class="paper-review-se__row-value">{{ row.value }}</span>
        </div>
        <div v-if="data.rows.length === 0" class="paper-review-se__empty tk-meta">
          No declared side-effects.
        </div>
      </div>

      <aside class="card paper-review-se__risk" data-testid="apply-risk-posture">
        <div class="tk-eyebrow paper-review-se__risk-eyebrow">Apply considerations</div>
        <div class="paper-review-se__risk-summary">{{ data.applyRisk.summary }}</div>
        <p class="paper-review-se__risk-desc">{{ data.applyRisk.description }}</p>
      </aside>
    </div>
  </section>
</template>

<style scoped>
.paper-review-se {
  margin-top: 28px;
}
.paper-review-se__header {
  display: flex;
  align-items: baseline;
  gap: 14px;
  margin-bottom: 10px;
  padding-bottom: 8px;
  border-bottom: 1px solid var(--line-soft);
}
.paper-review-se__serial {
  color: var(--faint);
}
.paper-review-se__title {
  margin: 0;
}
.paper-review-se__sub {
  margin-left: auto;
}
.paper-review-se__grid {
  display: grid;
  grid-template-columns: 1.4fr 1fr;
  gap: 14px;
}
.paper-review-se__rows {
  padding: 0;
  overflow: hidden;
}
.paper-review-se__row {
  display: grid;
  grid-template-columns: 140px 1fr;
  gap: 12px;
  padding: 10px 16px;
  border-bottom: 1px solid var(--line-soft);
  align-items: center;
}
.paper-review-se__row:last-child {
  border-bottom: 0;
}
.paper-review-se__row-key {
  color: var(--faint);
}
.paper-review-se__row[data-tone='active'] .paper-review-se__row-key {
  color: var(--ember);
}
.paper-review-se__row-value {
  font-size: 13px;
  color: var(--ink-2, var(--ink));
}
.paper-review-se__row[data-tone='active'] .paper-review-se__row-value {
  font-family: var(--serif);
  font-style: italic;
}
.paper-review-se__empty {
  padding: 16px;
}
.paper-review-se__risk {
  padding: 16px;
  background: var(--applied-tint);
  border-color: var(--applied);
}
.paper-review-se__risk-eyebrow {
  color: var(--applied);
}
.paper-review-se__risk-summary {
  font-family: var(--serif);
  font-size: 24px;
  font-style: italic;
  font-weight: 400;
  color: var(--ink-deep);
  margin: 6px 0 4px;
}
.paper-review-se__risk-desc {
  margin: 0;
  font-size: 12.5px;
  color: var(--ink-2, var(--ink));
}
</style>
