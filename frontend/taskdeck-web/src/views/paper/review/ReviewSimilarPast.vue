<script setup lang="ts">
import { computed, ref } from 'vue'
import PaperTagstamp from '../../../components/paper/PaperTagstamp.vue'
import type {
  SimilarPastRow,
} from '../../../composables/usePaperReviewSelectors'

/**
 * ReviewSimilarPast — list of 3 prior similar decisions with verdict
 * tagstamps and an aggregate apply-rate footer.
 *
 * Emptiness is stated on the card itself, never only inside the disclosure
 * (#1940): a reviewer must not have to open a control to learn that it has
 * nothing behind it. The disclosure stays present in every state because the
 * review view specs and the required E2E smoke drive it on an empty fixture.
 *
 * This layer cannot tell "still loading" from "loaded and empty" — it receives
 * rows and an apply rate, nothing about the fetch — so the empty copy is the
 * honest reading of an empty array here. Distinguishing the two needs a prop
 * from the rail (tracked on #1940).
 */
const props = defineProps<{
  rows: SimilarPastRow[]
  applyRate: { applied: number; total: number; ratio: number }
}>()

const isEmpty = computed(() => props.rows.length === 0)
const detailsExpanded = ref(false)
const detailsId = 'paper-review-similar-past-details'
const disclosureId = 'paper-review-similar-past-disclosure'
</script>

<template>
  <section class="card paper-review-past">
    <div class="tk-eyebrow paper-review-past__eyebrow">{{ $t('review.similarPast.heading') }}</div>
    <div
      v-if="isEmpty"
      class="tk-meta paper-review-past__empty"
      data-testid="paper-review-similar-past-empty"
    >
      {{ $t('review.similarPast.empty') }}
    </div>
    <button
      :id="disclosureId"
      type="button"
      class="paper-review-past__disclosure"
      data-testid="paper-review-similar-past-disclosure"
      :aria-controls="detailsId"
      :aria-expanded="detailsExpanded"
      @click="detailsExpanded = !detailsExpanded"
    >
      <span>{{
        detailsExpanded
          ? $t('review.similarPast.details.hide')
          : isEmpty
            ? $t('review.similarPast.details.showEmpty')
            : $t('review.similarPast.details.show')
      }}</span>
      <span aria-hidden="true">{{ detailsExpanded ? '−' : '+' }}</span>
    </button>
    <div
      v-show="detailsExpanded"
      :id="detailsId"
      data-testid="paper-review-similar-past-details"
      role="region"
      :aria-labelledby="disclosureId"
      :hidden="!detailsExpanded"
    >
      <p
        v-if="isEmpty"
        class="tk-meta paper-review-past__empty-detail"
        data-testid="paper-review-similar-past-empty-detail"
      >
        {{ $t('review.similarPast.emptyDetail') }}
      </p>
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
            {{ $t(`review.similarPast.verdict.${row.verdict}`) }}
          </PaperTagstamp>
          <div class="tk-meta paper-review-past__date">{{ row.date }}</div>
        </div>
      </div>
      <div v-if="rows.length > 0" class="tk-meta paper-review-past__rate">
        {{ $t('review.similarPast.rateLabel') }}
        <b>
          {{
            $t('review.similarPast.rateValue', {
              applied: applyRate.applied,
              total: applyRate.total,
              percent: Math.round(applyRate.ratio * 100),
            })
          }}
        </b>
      </div>
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
.paper-review-past__disclosure {
  width: 100%;
  min-height: 40px;
  padding: 6px 0;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  border: 0;
  border-top: 1px solid var(--line-soft);
  background: transparent;
  color: var(--ember);
  font: inherit;
  font-size: 11px;
  font-weight: 600;
  text-align: left;
  cursor: pointer;
}
.paper-review-past__disclosure:focus-visible {
  outline: 2px solid var(--ember);
  outline-offset: 3px;
}
.paper-review-past__empty {
  font-size: 11px;
  margin-bottom: 8px;
}
.paper-review-past__empty-detail {
  font-size: 11px;
  margin: 8px 0 0;
  line-height: 1.45;
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
