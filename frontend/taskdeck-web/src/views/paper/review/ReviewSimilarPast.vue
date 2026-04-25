<script setup lang="ts">
import PaperTagstamp from '../../../components/paper/PaperTagstamp.vue'
import type {
  SimilarPastRow,
} from '../../../composables/usePaperReviewSelectors'

/**
 * ReviewSimilarPast — list of 3 prior similar decisions with verdict
 * tagstamps and an aggregate apply-rate footer.
 */
defineProps<{
  rows: SimilarPastRow[]
  applyRate: { applied: number; total: number; ratio: number }
}>()
</script>

<template>
  <section class="card paper-review-past">
    <div class="tk-eyebrow paper-review-past__eyebrow">Similar past decisions</div>
    <div v-if="rows.length === 0" class="tk-meta paper-review-past__empty">
      No comparable past decisions.
    </div>
    <div
      v-for="row in rows"
      :key="row.serial"
      class="paper-review-past__row"
    >
      <div>
        <span class="tk-serial">{{ row.serial }}</span>
        <div class="paper-review-past__title">{{ row.title }}</div>
      </div>
      <div class="paper-review-past__verdict">
        <PaperTagstamp :tone="row.verdict === 'applied' ? 'applied' : 'overdue'">
          {{ row.verdict.toUpperCase() }}
        </PaperTagstamp>
        <div class="tk-meta paper-review-past__date">{{ row.date }}</div>
      </div>
    </div>
    <div v-if="rows.length > 0" class="tk-meta paper-review-past__rate">
      Apply rate on similar:
      <b>
        {{ applyRate.applied }} of {{ applyRate.total }}
        ({{ Math.round(applyRate.ratio * 100) }}%)
      </b>
    </div>
  </section>
</template>

<style scoped>
.paper-review-past {
  padding: 14px;
  margin-top: 12px;
}
.paper-review-past__eyebrow {
  margin-bottom: 8px;
}
.paper-review-past__empty {
  font-size: 11px;
}
.paper-review-past__row {
  display: grid;
  grid-template-columns: 1fr auto;
  padding: 6px 0;
  border-bottom: 1px dashed var(--line-soft);
  align-items: center;
}
.paper-review-past__row:last-child {
  border-bottom: 0;
}
.paper-review-past__title {
  font-size: 12px;
  color: var(--ink-2, var(--ink));
  line-height: 1.3;
}
.paper-review-past__verdict {
  text-align: right;
}
.paper-review-past__date {
  font-size: 10px;
  margin-top: 2px;
}
.paper-review-past__rate {
  font-size: 10px;
  margin-top: 8px;
}
.paper-review-past__rate b {
  color: var(--ink);
  font-weight: 500;
}
</style>
